namespace Looxdex.Api.Configuration;

public class PexelsOptions
{
    public const string SectionName = "Pexels";

    /// <summary>Pexels API key. Supply via the PEXELS__APIKEY environment variable in production.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether to keep pulling stock photography into the feed.
    ///
    /// Off: the feed is the wearer's own looks now, and a background job that
    /// quietly refills it with stock every six hours would undo that on its own.
    /// Deleting the key is not enough — it lives in the deployment's environment,
    /// not in the repository — so the switch has to be here.
    /// </summary>
    public bool Ingest { get; set; }

    public bool Enabled => Ingest && !string.IsNullOrWhiteSpace(ApiKey);

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

    /// <summary>
    /// Preprocessing target, DETR-style: the shortest edge is scaled to this
    /// unless that would push the longest past <see cref="LongestEdge"/>.
    /// The model's input is dynamic, so the aspect ratio is preserved rather
    /// than stretching every photo into one fixed frame.
    /// </summary>
    public int ShortestEdge { get; set; } = 640;

    public int LongestEdge { get; set; } = 1024;

    /// <summary>
    /// Drop detections below this confidence.
    ///
    /// Back down to 0.45 now that the segmenter vets every hit. The 0.45–0.6 band
    /// is where the false positives live — bare feet read as "shoe", skin as "top"
    /// — but it is also where real watches, belts and half-turned shirts live, and
    /// those used to be thrown away with the rest. A detection in that band is now
    /// kept only if a second model finds the garment where the box says it is.
    /// </summary>
    public double MinScore { get; set; } = 0.45;

    /// <summary>
    /// Confidence at which a detection is believed even when the segmenter cannot
    /// find the garment. Segmentation has its own failures — an unusual cut, a
    /// garment against its own colour — and they should not erase a detection the
    /// detector is this sure about.
    /// </summary>
    public double UnconfirmedScore { get; set; } = 0.80;

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

/// <summary>
/// Cutting the detected garment out of the photo, so an item reads as a product
/// shot rather than a slice of someone's holiday snap.
///
/// A separate model from the detector: YOLOS gives boxes and labels, this gives
/// per-pixel garment classes. Both are needed — the box says which instance we
/// mean, the mask says which pixels are actually the garment rather than the
/// wearer's hands, face and background.
/// </summary>
public class GarmentCutoutOptions
{
    public const string SectionName = "GarmentCutout";

    public bool Enabled { get; set; } = true;

    /// <summary>SegFormer-B2 fine-tuned on ATR (18 clothing classes), quantised to ~29MB.</summary>
    public string ModelUrl { get; set; } =
        "https://huggingface.co/Xenova/segformer_b2_clothes/resolve/main/onnx/model_quantized.onnx";

    public string FileName { get; set; } = "segformer-b2-clothes-quantized.onnx";

    /// <summary>
    /// Segmentation input. The processor was exported at 512, which puts one mask
    /// cell every ten pixels of the photo — an outline accurate to about a finger's
    /// width, which is exactly what made the tiles look torn out rather than cut.
    /// SegFormer takes any multiple of 32, and doubling it quarters the cell.
    /// </summary>
    public int InputSize { get; set; } = 1024;

    /// <summary>Side of the square tile written for each item.</summary>
    public int TileSize { get; set; } = 768;

    /// <summary>
    /// Radius of the edge refinement, in pixels of the cropped garment. The coarse
    /// mask is pulled onto the real boundary by looking at where the photo's own
    /// colours change; this is how far it may look to find that boundary.
    /// </summary>
    public int EdgeRefineRadius { get; set; } = 10;

    /// <summary>
    /// Shortest side a garment must occupy in the photo before a cutout is worth
    /// making. Below it there is nothing to enlarge — the tile would be a blur,
    /// and a plain crop at least reads as a photograph of something.
    /// </summary>
    public int MinGarmentPixels { get; set; } = 220;

    /// <summary>
    /// How much of its own outline a garment must fill to be shown as a cutout.
    /// Below this the shape is fragments — a sleeve here, a trouser leg there —
    /// and no amount of sharpening makes fragments legible.
    /// </summary>
    public double MinShapeFill { get; set; } = 0.38;

    /// <summary>
    /// Confidence required when there is no one in the photo. The models are both
    /// out of their depth on a flat-lay, and this is what stops a handbag on a bed
    /// being filed, with a straight face, as a pair of trousers.
    /// </summary>
    public double FlatLayScore { get; set; } = 0.90;

    /// <summary>
    /// The floor under which even a flat-lay's best guess is not worth keeping.
    /// Above it the item is kept unnamed, for the shops to identify.
    /// </summary>
    public double FlatLayFloor { get; set; } = 0.40;

    /// <summary>Transparent margin inside the tile, as a fraction of its side.</summary>
    public double TileInset { get; set; } = 0.09;

    /// <summary>
    /// Below this many mask pixels the cutout is thin, blocky and worse than the
    /// plain crop — belts and thin straps land here — so no tile is written.
    /// </summary>
    public int MinMaskPixels { get; set; } = 20000;

    /// <summary>Posts per pass of the cutout backfill.</summary>
    public int BackfillBatchSize { get; set; } = 8;
}

/// <summary>
/// The hand-authored demo content: a starter closet, demo suitcases.
///
/// Off. It existed so the app had something to show before anyone had added
/// anything, and it now sits alongside the wearer's real looks looking like
/// stock photography, because that is what it is.
/// </summary>
public class DemoContentOptions
{
    public const string SectionName = "DemoContent";

    public bool Enabled { get; set; }
}

/// <summary>
/// Matching a detected garment to a real product photograph.
///
/// This is the answer to a cut-out that nobody can read: rather than sharpening a
/// scrap of somebody's holiday snap, show the shop's picture of the same kind of
/// thing. Off by default, because it wants a third model in memory and a
/// catalogue to match against.
/// </summary>
public class ProductMatchOptions
{
    public const string SectionName = "ProductMatch";

    public bool Enabled { get; set; } = true;

    /// <summary>CLIP ViT-B/32 vision tower, quantised.</summary>
    public string ModelUrl { get; set; } =
        "https://huggingface.co/Xenova/clip-vit-base-patch32/resolve/main/onnx/vision_model_quantized.onnx";

    public string FileName { get; set; } = "clip-vit-base-patch32-vision-quantized.onnx";

    /// <summary>How many products to keep per detected item.</summary>
    public int Alternatives { get; set; } = 4;

    /// <summary>
    /// Similarity a product must reach to be offered at all. Below it the nearest
    /// thing in the catalogue is not the same garment, and showing it anyway is
    /// worse than showing nothing.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.86;

    /// <summary>Products pulled per page while filling the catalogue.</summary>
    public int IngestPageSize { get; set; } = 100;
}

/// <summary>
/// Reverse image search, for finding the garment on sale somewhere.
///
/// Preferred over matching against our own catalogue whenever a key is
/// configured: it returns shops the wearer can actually buy from, with today's
/// prices, rather than the nearest thing in a fixed dataset.
/// </summary>
public class VisualSearchOptions
{
    public const string SectionName = "VisualSearch";

    public bool Enabled { get; set; } = true;

    /// <summary>Supply via user-secrets locally, VISUALSEARCH__APIKEY in the environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>How many shop results to ask for before checking them.</summary>
    public int MaxResults { get; set; } = 12;

    /// <summary>
    /// How alike a shop's photograph must be to a crop of the look before it is
    /// offered. Google is generous about what counts as similar; this is where
    /// that generosity is spent rather than passed on.
    /// </summary>
    public double MinSimilarity { get; set; } = 0.80;

    /// <summary>
    /// The same bar, for when the search was made with a cut-out.
    ///
    /// Lower on purpose. A cut-out is a garment with its wearer erased, standing
    /// on nothing; a shop's photograph is the same garment lit, pressed and hung.
    /// They score further apart than two photographs of a street do, whatever they
    /// are of — so holding both to one bar quietly rejects the cut-outs. A pair of
    /// brown trousers came back from twelve shops and scored 0.791 against a bar of
    /// 0.80, and every one of the twelve was thrown away.
    /// </summary>
    public double MinCutoutSimilarity { get; set; } = 0.72;

    /// <summary>
    /// How sure the detector has to have been before the shops are searched.
    /// Lookups are metered, and a doubtful detection is the one least likely to
    /// be worth one.
    /// </summary>
    public double MinDetectionScore { get; set; } = 0.75;

    /// <summary>
    /// Where our images can be reached from the outside.
    ///
    /// The search fetches the picture itself, so the address it is given has to be
    /// one the internet can resolve. Matching run from a laptop against the shared
    /// database would otherwise hand Google "http://localhost:5247/..." and be told,
    /// accurately, that there are no results.
    /// </summary>
    public string PublicBaseUrl { get; set; } = string.Empty;
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
