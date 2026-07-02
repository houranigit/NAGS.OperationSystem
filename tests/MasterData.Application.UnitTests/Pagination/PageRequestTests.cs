using BuildingBlocks.Application.Pagination;
using Shouldly;

namespace MasterData.Application.UnitTests.Pagination;

public sealed class PageRequestTests
{
    [Fact]
    public void From_normalizes_low_page_and_page_size_values()
    {
        var paging = PageRequest.From(-10, 0);

        paging.Page.ShouldBe(1);
        paging.PageSize.ShouldBe(1);
        paging.Skip.ShouldBe(0);
    }

    [Fact]
    public void From_clamps_page_size_and_caps_page_before_skip_overflows()
    {
        var paging = PageRequest.From(int.MaxValue, int.MaxValue);

        paging.PageSize.ShouldBe(PageRequest.MaxPageSize);
        paging.Page.ShouldBe((int.MaxValue / PageRequest.MaxPageSize) + 1);
        paging.Skip.ShouldBeLessThanOrEqualTo(int.MaxValue);
    }

    [Fact]
    public void IsOutOfRange_detects_empty_and_past_end_pages()
    {
        PageRequest.From(1, 20).IsOutOfRange(0).ShouldBeTrue();
        PageRequest.From(10, 20).IsOutOfRange(100).ShouldBeTrue();
        PageRequest.From(5, 20).IsOutOfRange(100).ShouldBeFalse();
    }

    [Fact]
    public void Paged_result_total_pages_clamps_large_totals()
    {
        var result = new PagedResult<int>([], 1, 1, long.MaxValue);

        result.TotalPages.ShouldBe(int.MaxValue);
        result.HasNextPage.ShouldBeTrue();
    }
}
