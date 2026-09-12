namespace Looxdex.Api.Configuration;

public class PexelsOptions
{
    public const string SectionName = "Pexels";

    /// <summary>Pexels API key. Supply via the PEXELS__APIKEY environment variable in production.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiKey);

    /// <summary>Queries rotated through by the ingest job to keep the feed varied.</summary>
    public List<string> Queries { get; set; } = new()
    {
        "street style fashion",
        "outfit of the day",
        "minimalist fashion",
        "parisian style",
        "autumn outfit",
        "summer outfit women",
        "business casual outfit",
        "evening dress elegant"
    };

    /// <summary>Photos requested per query per run. Pexels caps per_page at 80.</summary>
    public int PerPage { get; set; } = 40;

    /// <summary>How often the background ingest runs. Zero disables the timer.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Stop ingesting once the library reaches this many posts.</summary>
    public int MaxLibrarySize { get; set; } = 5000;
}

/// <summary>
/// Fashion detection running locally through ONNX Runtime.
///
/// Free hosted inference for fashion-specific models no longer exists — Hugging
/// Face's serverless provider only keeps generic COCO detectors warm — so the
/// model runs in-process on the background worker instead.
/// </summary>
public class OnnxDetectionOptions
{
    public const string SectionName = "OnnxDetection";

    public bool Enabled { get; set; } = true;

    public string ModelUrl { get; set; } =
        "https://huggingface.co/onnx-community/yolos-fashionpedia-ONNX/resolve/main/onnx/model_quantized.onnx";

    /// <summary>
    /// Not "models": Windows paths are case-insensitive, so that collides with
    /// the project's own Models/ source folder and drops a 57MB blob into it.
    /// </summary>
    public string CacheDirectory { get; set; } = ".model-cache";
    public string FileName { get; set; } = "yolos-fashionpedia-quantized.onnx";

    /// <summary>Model input size, height then width, from the model's config.json.</summary>
    public int InputHeight { get; set; } = 512;
    public int InputWidth { get; set; } = 864;

    /// <summary>
    /// Drop detections below this confidence.
    ///
    /// Tuned up from 0.45: genuine detections land at 0.7–1.0, while the 0.45–0.6
    /// band is mostly false positives — bare feet read as "shoe", skin as "top".
    /// A missing item is far less damaging here than a confidently wrong one.
    /// </summary>
    public double MinScore { get; set; } = 0.65;

    /// <summary>Keep at most this many items per image, highest score first.</summary>
    public int MaxItemsPerImage { get; set; } = 6;

    /// <summary>Posts processed per sweep. CPU inference is slow, so keep batches small.</summary>
    public int BatchSize { get; set; } = 4;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Give up on an image after this many failed attempts.</summary>
    public int MaxAttempts { get; set; } = 3;

    /// <summary>ONNX Runtime threads. One keeps memory and CPU predictable on a small instance.</summary>
    public int IntraOpThreads { get; set; } = 1;
}

public class HuggingFaceOptions
{
    public const string SectionName = "HuggingFace";

    /// <summary>HF access token. Supply via HUGGINGFACE__APITOKEN in production.</summary>
    public string ApiToken { get; set; } = string.Empty;

    public bool Enabled => !string.IsNullOrWhiteSpace(ApiToken);

    /// <summary>Fashion object detection model, fine-tuned on Fashionpedia.</summary>
    public string DetectionModel { get; set; } = "valentinafeve/yolos-fashionpedia";

    /// <summary>CLIP variant used for closet/detection similarity.</summary>
    public string EmbeddingModel { get; set; } = "sentence-transformers/clip-ViT-B-32";

    /// <summary>Drop detections below this confidence.</summary>
    public double MinScore { get; set; } = 0.55;

    /// <summary>Keep at most this many items per image, highest score first.</summary>
    public int MaxItemsPerImage { get; set; } = 6;

    /// <summary>Posts processed per detection sweep; keeps well inside the free tier.</summary>
    public int BatchSize { get; set; } = 8;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Give up on an image after this many failed detection attempts.</summary>
    public int MaxAttempts { get; set; } = 3;
}
