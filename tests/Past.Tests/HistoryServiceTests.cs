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

    [Fact]
    public async Task Capture_stores_new_text_clip()
    {
        var svc = Build(out var store, out _);

        var outcome = await svc.CaptureAsync(new ClipDraft("hello", "notepad"));

        Assert.Equal(CaptureOutcome.Added, outcome);
        var only = Assert.Single(store.All);
        Assert.Equal("hello", only.Content);
        Assert.Equal("notepad", only.SourceApp);
    }

    [Fact]
    public async Task Duplicate_content_is_deduped_and_moved_to_top()
    {
        var svc = Build(out var store, out var clock);

        await svc.CaptureAsync(new ClipDraft("same", null));
        await svc.CaptureAsync(new ClipDraft("other", null));
        clock.Advance(TimeSpan.FromMinutes(1));
        var outcome = await svc.CaptureAsync(new ClipDraft("same", null));

        Assert.Equal(CaptureOutcome.Deduped, outcome);
        Assert.Equal(2, store.All.Count); // no third row

        var recent = await svc.GetRecentAsync();
        Assert.Equal("same", recent[0].Content); // dedupe bumped it back to the top
    }

    [Fact]
    public async Task Empty_or_whitespace_is_skipped()
    {
        var svc = Build(out var store, out _);

        Assert.Equal(CaptureOutcome.Skipped, await svc.CaptureAsync(new ClipDraft("   ", null)));
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Oversize_content_is_skipped()
    {
        var svc = Build(out var store, out _, new HistoryOptions { MaxItemChars = 5 });

        Assert.Equal(CaptureOutcome.Skipped, await svc.CaptureAsync(new ClipDraft("toolong", null)));
        Assert.Empty(store.All);
    }

    [Fact]
    public async Task Count_cap_trims_oldest()
    {
        var svc = Build(out var store, out var clock, new HistoryOptions { MaxItems = 2 });

        foreach (var s in new[] { "a", "b", "c" })
        {
            await svc.CaptureAsync(new ClipDraft(s, null));
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
        await svc.CaptureAsync(new ClipDraft("Hello World", null));
        await svc.CaptureAsync(new ClipDraft("goodbye", null));

        var hits = await svc.SearchAsync("WORLD");

        var hit = Assert.Single(hits);
        Assert.Equal("Hello World", hit.Content);
    }
}
