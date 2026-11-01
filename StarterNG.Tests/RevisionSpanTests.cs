using StarterNG.Domain.Vehicles;

namespace StarterNG.Tests;

public class RevisionSpanTests
{
    [Theory]
    [InlineData("14.01.2005", "2005-01-14", "2005-01-14")]
    [InlineData("06.12.2016", "2016-12-06", "2016-12-06")]
    [InlineData("xx.06.2008", "2008-06-01", "2008-06-30")]
    [InlineData("?.06.2008", "2008-06-01", "2008-06-30")]
    [InlineData("xx.xx.2011", "2011-01-01", "2011-12-31")]
    [InlineData("?.?.2011", "2011-01-01", "2011-12-31")]
    [InlineData("02.2016", "2016-02-01", "2016-02-29")]
    public void A_credit_line_date_is_as_exact_as_it_says(string text, string from, string to)
    {
        var span = RevisionSpan.Parse(text);

        Assert.Equal(DateTime.Parse(from), span?.From);
        Assert.Equal(DateTime.Parse(to), span?.To);
    }

    [Theory]
    [InlineData("2016", "2016-01-01", "2016-12-31")]
    [InlineData("2016-12", "2016-12-01", "2016-12-31")]
    [InlineData("2016-12-06", "2016-12-06", "2016-12-06")]
    [InlineData(" 2016-12- ", "2016-12-01", "2016-12-31")]
    public void What_is_typed_to_filter_by_reads_the_same_way(string text, string from, string to)
    {
        var span = RevisionSpan.Parse(text);

        Assert.Equal(DateTime.Parse(from), span?.From);
        Assert.Equal(DateTime.Parse(to), span?.To);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("xx.xx.xxxx")]
    [InlineData("31.02.2015")]
    [InlineData("01.13.2015")]
    [InlineData("VRZ-99_Kolowag_Septemwri")]
    [InlineData("201")]
    public void Anything_else_is_no_date(string? text) =>
        Assert.Null(RevisionSpan.Parse(text));
}
