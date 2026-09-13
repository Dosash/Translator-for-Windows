using Microsoft.ML.OnnxRuntime;

namespace Translator.Offline;

/// <summary>ONNX Runtime session knobs (exposed for benchmarking through OfflineCli).</summary>
internal sealed record InferenceSettings(int IntraOpThreads, bool UseCpuArena = true, bool Prepacking = true, bool AllowSpinning = false);

/// <summary>
/// One loaded translation direction: tokenizer, encoder session and merged decoder session with KV cache.
/// Greedy decoding; calls are serialized per model.
/// </summary>
internal sealed class MarianModel : IDisposable
{
    private const string PastPrefix = "past_key_values";
    private const string PresentPrefix = "present";

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string[] _encoderOutputNames;
    private readonly bool _encoderTakesMask;
    private readonly bool _decoderTakesEncoderMask;
    private readonly string[] _pastNames;
    private readonly bool[] _isEncoderPast;
    private readonly string[] _decoderOutputNames;
    private readonly bool[] _suppressed;

    private MarianModel(string id, MarianTokenizer tokenizer, MarianConfig config, InferenceSession encoder, InferenceSession decoder)
    {
        Id = id;
        Tokenizer = tokenizer;
        Config = config;
        _encoder = encoder;
        _decoder = decoder;

        _encoderOutputNames = [encoder.OutputMetadata.ContainsKey("last_hidden_state") ? "last_hidden_state" : encoder.OutputMetadata.Keys.First()];
        _encoderTakesMask = encoder.InputMetadata.ContainsKey("attention_mask");
        _decoderTakesEncoderMask = decoder.InputMetadata.ContainsKey("encoder_attention_mask");
        _pastNames = decoder.InputMetadata.Keys.Where(name => name.StartsWith(PastPrefix + ".", StringComparison.Ordinal)).ToArray();
        if (_pastNames.Length == 0 || !decoder.InputMetadata.ContainsKey("use_cache_branch"))
        {
            throw new NotSupportedException("The decoder must be a merged ONNX decoder with a KV cache.");
        }

        _isEncoderPast = _pastNames.Select(name => name.Contains(".encoder.", StringComparison.Ordinal)).ToArray();
        var presentNames = _pastNames.Select(name => PresentPrefix + name[PastPrefix.Length..]).ToArray();
        var missing = presentNames.FirstOrDefault(name => !decoder.OutputMetadata.ContainsKey(name));
        if (missing is not null)
        {
            throw new NotSupportedException($"The decoder has no output {missing}.");
        }

        _decoderOutputNames = ["logits", .. presentNames];

        var vocabularySize = decoder.OutputMetadata["logits"].Dimensions is { Length: 3 } dims && dims[2] > 0 ? dims[2] : 0;
        _suppressed = new bool[Math.Max(vocabularySize, config.SuppressedTokenIds.Append(0).Max() + 1)];
        foreach (var tokenId in config.SuppressedTokenIds)
        {
            _suppressed[tokenId] = true;
        }
    }

    public string Id { get; }

    public MarianTokenizer Tokenizer { get; }

    public MarianConfig Config { get; }

    public static MarianModel Load(string directory, OfflineModelInfo info, InferenceSettings settings)
    {
        var tokenizer = MarianTokenizer.Load(directory, info.SourceIdsFromSentencePiece);
        var config = MarianConfig.Load(directory);
        using var options = CreateSessionOptions(settings);
        InferenceSession? encoder = null;
        try
        {
            encoder = new InferenceSession(ModelInstaller.GetLocalPath(directory, info.EncoderPath), options);
            var decoder = new InferenceSession(ModelInstaller.GetLocalPath(directory, info.DecoderPath), options);
            try
            {
                return new MarianModel(info.Id, tokenizer, config, encoder, decoder);
            }
            catch
            {
                decoder.Dispose();
                throw;
            }
        }
        catch
        {
            encoder?.Dispose();
            throw;
        }
    }

    public string Translate(string text, string? targetToken, CancellationToken cancellationToken)
    {
        _gate.Wait(cancellationToken);
        try
        {
            var inputIds = Tokenizer.Encode(text, targetToken);
            return Tokenizer.Decode(Generate(inputIds, cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    internal int[] Generate(int[] sourceIds, CancellationToken cancellationToken)
    {
        var sourceLength = sourceIds.Length;
        // max_length counts the decoder start token; the 3x cap stops degenerate repetition early.
        var maxNewTokens = Math.Max(1, Math.Min(Config.MaxLength - 1, 3 * sourceLength + 16));
        long[] sourceShape = [1, sourceLength];
        var inputIds = Array.ConvertAll(sourceIds, static id => (long)id);
        var mask = new long[sourceLength];
        Array.Fill(mask, 1L);

        using var runOptions = new RunOptions();
        using var registration = cancellationToken.Register(static state => ((RunOptions)state!).Terminate = true, runOptions);
        using var inputIdsValue = OrtValue.CreateTensorValueFromMemory(inputIds, sourceShape);
        using var maskValue = OrtValue.CreateTensorValueFromMemory(mask, sourceShape);

        var encoderInputs = new Dictionary<string, OrtValue> { ["input_ids"] = inputIdsValue };
        if (_encoderTakesMask)
        {
            encoderInputs["attention_mask"] = maskValue;
        }

        var encoderOutputs = Run(_encoder, runOptions, encoderInputs, _encoderOutputNames, cancellationToken);
        var past = new OrtValue?[_pastNames.Length];
        try
        {
            var hiddenStates = encoderOutputs[0]!;
            for (var k = 0; k < past.Length; k++)
            {
                past[k] = CreateEmptyPast(_pastNames[k]);
            }

            var generated = new List<int>(Math.Min(maxNewTokens, 256));
            var token = new[] { (long)Config.DecoderStartTokenId };
            var useCache = new bool[1];
            long[] tokenShape = [1, 1];
            long[] flagShape = [1];
            var inputs = new Dictionary<string, OrtValue>(past.Length + 4);

            for (var step = 0; step < maxNewTokens; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                useCache[0] = step > 0;
                using var tokenValue = OrtValue.CreateTensorValueFromMemory(token, tokenShape);
                using var useCacheValue = OrtValue.CreateTensorValueFromMemory(useCache, flagShape);
                inputs.Clear();
                inputs["input_ids"] = tokenValue;
                inputs["encoder_hidden_states"] = hiddenStates;
                inputs["use_cache_branch"] = useCacheValue;
                if (_decoderTakesEncoderMask)
                {
                    inputs["encoder_attention_mask"] = maskValue;
                }

                for (var k = 0; k < past.Length; k++)
                {
                    inputs[_pastNames[k]] = past[k]!;
                }

                var outputs = Run(_decoder, runOptions, inputs, _decoderOutputNames, cancellationToken);
                int next;
                try
                {
                    next = SelectNextToken(outputs[0]!);
                    for (var k = 0; k < past.Length; k++)
                    {
                        // In the cache branch the encoder cache outputs are placeholders; the first step's tensors stay valid.
                        if (_isEncoderPast[k] && step > 0)
                        {
                            continue;
                        }

                        past[k]!.Dispose();
                        past[k] = outputs[k + 1];
                        outputs[k + 1] = null;
                    }
                }
                finally
                {
                    DisposeAll(outputs);
                }

                if (next == Config.EosTokenId)
                {
                    break;
                }

                generated.Add(next);
                token[0] = next;
            }

            return [.. generated];
        }
        finally
        {
            DisposeAll(past);
            DisposeAll(encoderOutputs);
        }
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
        _gate.Dispose();
    }

    private static SessionOptions CreateSessionOptions(InferenceSettings settings)
    {
        var options = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
            IntraOpNumThreads = settings.IntraOpThreads,
            InterOpNumThreads = 1,
            EnableCpuMemArena = settings.UseCpuArena,
        };
        // Busy-waiting worker threads would keep cores hot after every translation in a tray app.
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", settings.AllowSpinning ? "1" : "0");
        if (!settings.Prepacking)
        {
            options.AddSessionConfigEntry("session.disable_prepacking", "1");
        }

        return options;
    }

    private static OrtValue?[] Run(
        InferenceSession session,
        RunOptions runOptions,
        IReadOnlyDictionary<string, OrtValue> inputs,
        string[] outputNames,
        CancellationToken cancellationToken)
    {
        try
        {
            // The returned collection is intentionally not disposed: ownership of each value moves to the caller,
            // which keeps the KV cache tensors alive across decoding steps.
            var results = session.Run(runOptions, inputs, outputNames);
            return results.Select(static value => (OrtValue?)value).ToArray();
        }
        catch (OnnxRuntimeException) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
    }

    private OrtValue CreateEmptyPast(string name)
    {
        var metadata = _decoder.InputMetadata[name];
        var dims = metadata.Dimensions;
        var heads = dims.Length > 1 && dims[1] > 0 ? dims[1] : Config.DecoderAttentionHeads;
        var headSize = dims.Length > 3 && dims[3] > 0 ? dims[3] : Config.DModel / Config.DecoderAttentionHeads;
        return OrtValue.CreateAllocatedTensorValue(OrtAllocator.DefaultInstance, metadata.ElementDataType, [1, heads, 0, headSize]);
    }

    private int SelectNextToken(OrtValue logits)
    {
        var shape = logits.GetTensorTypeAndShape().Shape;
        var vocabularySize = (int)shape[^1];
        var row = logits.GetTensorDataAsSpan<float>()[^vocabularySize..];
        var best = -1;
        var bestScore = float.NegativeInfinity;
        for (var k = 0; k < row.Length; k++)
        {
            if (row[k] > bestScore && (k >= _suppressed.Length || !_suppressed[k]))
            {
                best = k;
                bestScore = row[k];
            }
        }

        return best < 0 ? Config.EosTokenId : best;
    }

    private static void DisposeAll(OrtValue?[] values)
    {
        foreach (var value in values)
        {
            value?.Dispose();
        }
    }
}
