using DeBoetAPI.Common;

namespace DeBoetAPI.Tests;

public class PagingTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 1)] 
    [InlineData(-5, -5, 1, 1)]
    [InlineData(3, 50, 3, 50)]
    [InlineData(1, 5000, 1, 100)] 
    public void Normalize_ClampsToServableRange(int? page, int? pageSize, int expectedPage, int expectedSize)
    {
        var (actualPage, actualSize) = Paging.Normalize(page, pageSize);

        Assert.Equal(expectedPage, actualPage);
        Assert.Equal(expectedSize, actualSize);
    }

    [Fact]
    public void TotalPages_RoundsUpPartialPage()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 20, TotalCount: 41);

        Assert.Equal(3, result.TotalPages);
    }

    [Fact]
    public void TotalPages_IsZeroWhenEmpty()
    {
        var result = new PagedResult<string>([], Page: 1, PageSize: 20, TotalCount: 0);

        Assert.Equal(0, result.TotalPages);
    }
}
