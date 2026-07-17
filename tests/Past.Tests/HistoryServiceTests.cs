using Past.Core;
using Past.Services;

namespace Past.Tests;

public class HistoryServiceTests
{
    private static HistoryService Build(out InMemoryClipStore store, out FakeClock clock, HistoryOptions? opts = null)
    {
        store = new InMemoryClipStore();
        clock = new FakeClock();
        return new HistoryService(store, clock, opts ?? new HistoryOptions());
    }

    private static ClipDraft Text(string s, string? app = null) => ClipDraft.ForText(s, app);

    private static ClipDraft Image(byte[] bytes, int w = 800, int h = 600) =>
        ClipDraft.ForImage(bytes, thumbnailBytes: [9, 9, 9], pixelWidth: w, pixelHeight: h, sourceApp: "chrome");

    [Fact]
    public async Task Capture_stores_new_text_clip()
    {
        var svc = Build(out var store, out _);

        var outcome = await svc.CaptureAsync(Text("hello", "notepad"));

        Assert.Equal(CaptureOutcome.Added, outcome);
        var only = Assert.Single(store.All);
        Assert.Equal("hello", only.Content);
        Assert.Equal("notepad", only.SourceApp);
        Assert.Equal(ClipContentType.Text, only.ContentType);
    }

    [Fact]
    public async Task Duplicate_content_is_deduped_and_moved_to_top()
    {
        var svc = Build(out var store, out var clock);

        await svc.CaptureAsync(Text("same"));
        await svc.CaptureAsync(Text("other"));
        clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await svc.CaptureAsync(Text("same"));

        Assert.Equal(CaptureOutcome.Deduped, outcome);
        Assert.Equal(2, store.All.Count); // no third row

        var recent = await svc.GetRecentAsync();
        Assert.Equal("same", recent[0].Content); // dedupe bumped it back to the top
    }

    [Fact]
    public async Task Empty_or_whitespace_is_skipped()
    {
        var svc = Build(out var store, out _);

        Assert.Equal(CaptureOutcome.Skipped, await svc.CaptureAsync(Text("   ")));
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Oversize_content_is_skipped()
    {
        var svc = Build(out var store, out _, new HistoryOptions { MaxItemChars = 5 });

        Assert.Equal(CaptureOutcome.Skipped, await svc.CaptureAsync(Text("toolong")));
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Count_cap_trims_oldest()
    {
        var svc = Build(out var store, out var clock, new HistoryOptions { MaxItems = 2 });

        foreach (var s in new[] { "a", "b", "c" })
        {
            await svc.CaptureAsync(Text(s));
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(2, store.All.Count);
        var recent = await svc.GetRecentAsync();
        Assert.Equal(new[] { "c", "b" }, recent.Select(c => c.Content).ToArray());
    }

    [Fact]
    public async Task Search_filters_by_substring_case_insensitive()
    {
        var svc = Build(out _, out _);
        await svc.CaptureAsync(Text("Hello World"));
        await svc.CaptureAsync(Text("goodbye"));

        var hits = await svc.SearchAsync("WORLD");

        var hit = Assert.Single(hits);
        Assert.Equal("Hello World", hit.Content);
    }

    [Fact]
    public async Task Capture_stores_image_clip_with_dimensions_and_thumbnail()
    {
        var svc = Build(out var store, out _);

        var outcome = await svc.CaptureAsync(Image([1, 2, 3, 4], 1920, 1080));

        Assert.Equal(CaptureOutcome.Added, outcome);
        var clip = Assert.Single(store.All);
        Assert.Equal(ClipContentType.Image, clip.ContentType);
        Assert.Equal([1, 2, 3, 4], clip.Data);
        Assert.Equal([9, 9, 9], clip.Thumbnail);
        Assert.Equal(1920, clip.PixelWidth);
        Assert.Equal(1080, clip.PixelHeight);
        Assert.Equal(4, clip.SizeBytes);
        Assert.Equal("Image 1920×1080", clip.Preview);
    }

    [Fact]
    public async Task Identical_images_are_deduped_by_bytes()
    {
        var svc = Build(out var store, out var clock);

        await svc.CaptureAsync(Image([1, 2, 3]));
        await svc.CaptureAsync(Image([4, 5, 6]));
        clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await svc.CaptureAsync(Image([1, 2, 3]));

        Assert.Equal(CaptureOutcome.Deduped, outcome);
        Assert.Equal(2, store.All.Count);
    }

    [Fact]
    public async Task Empty_image_is_skipped()
    {
        var svc = Build(out var store, out _);

        Assert.Equal(CaptureOutcome.Skipped, await svc.CaptureAsync(Image([])));
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Image_is_not_subject_to_the_text_char_cap()
    {
        // MaxItemChars is a text rule; a small char cap must not reject images.
        var svc = Build(out var store, out _, new HistoryOptions { MaxItemChars = 1 });

        Assert.Equal(CaptureOutcome.Added, await svc.CaptureAsync(Image([1, 2, 3, 4, 5, 6])));
        Assert.Single(store.All);
    }

    [Fact]
    public async Task Image_without_thumbnail_is_still_captured()
    {
        // Images over the thumbnail cap arrive with no thumbnail; they must still be stored.
        var svc = Build(out var store, out _);

        var draft = ClipDraft.ForImage([1, 2, 3], thumbnailBytes: null, pixelWidth: 9000, pixelHeight: 9000, sourceApp: null);
        Assert.Equal(CaptureOutcome.Added, await svc.CaptureAsync(draft));

        var clip = Assert.Single(store.All);
        Assert.Null(clip.Thumbnail);
        Assert.NotNull(clip.Data);
    }
}
