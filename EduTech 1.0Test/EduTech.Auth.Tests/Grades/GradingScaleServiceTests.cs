using EduTech.Grades.ReportCards;
using EduTech.Shared.Exceptions;
using Moq;

namespace EduTech.Auth.Tests.Grades;

public class GradingScaleServiceTests
{
    private readonly Mock<IGradingScaleRepository> _repo = new();
    private GradingScaleService CreateSut() => new(_repo.Object);

    [Fact]
    public async Task Get_NoneSaved_ReturnsDefaultFiveBands()
    {
        _repo.Setup(r => r.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<GradeBoundaryRow>());
        IReadOnlyList<GradeBoundaryDto> scale = await CreateSut().GetAsync(CancellationToken.None);
        Assert.Equal(5, scale.Count);
        Assert.Equal("A", scale[0].Grade);
    }

    [Fact]
    public async Task Save_Empty_Throws400()
    {
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(new SaveGradingScaleRequest { Bands = new() }, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Save_BadRange_Throws400()
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new() { new GradeBoundaryDto { MinScore = 60, MaxScore = 40, Grade = "A", Remark = "x" } }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Save_Valid_Replaces()
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new() { new GradeBoundaryDto { MinScore = 0, MaxScore = 100, Grade = "Pass", Remark = "OK" } }
        };
        await CreateSut().SaveAsync(req, CancellationToken.None);
        _repo.Verify(r => r.ReplaceAsync(It.Is<IReadOnlyList<GradeBoundaryDto>>(b => b.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_DefaultFiveBands_Succeeds()
    {
        await CreateSut().SaveAsync(
            new SaveGradingScaleRequest { Bands = GradingScale.Defaults.ToList() }, CancellationToken.None);
        _repo.Verify(r => r.ReplaceAsync(It.Is<IReadOnlyList<GradeBoundaryDto>>(b => b.Count == 5), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_OverlappingBands_Throws400()
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new()
            {
                new GradeBoundaryDto { MinScore = 0, MaxScore = 50, Grade = "F", Remark = "Fail" },
                new GradeBoundaryDto { MinScore = 40, MaxScore = 100, Grade = "A", Remark = "Pass" }
            }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Save_GapBetweenBands_Throws400()
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new()
            {
                new GradeBoundaryDto { MinScore = 0, MaxScore = 39, Grade = "F", Remark = "Fail" },
                new GradeBoundaryDto { MinScore = 50, MaxScore = 100, Grade = "A", Remark = "Pass" }
            }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData(10, 100)]   // doesn't start at 0
    [InlineData(0, 90)]     // doesn't reach 100
    public async Task Save_NotCoveringFullScale_Throws400(int min, int max)
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new() { new GradeBoundaryDto { MinScore = min, MaxScore = max, Grade = "P", Remark = "x" } }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task Save_DuplicateGradeLabel_Throws400()
    {
        SaveGradingScaleRequest req = new SaveGradingScaleRequest
        {
            Bands = new()
            {
                new GradeBoundaryDto { MinScore = 0, MaxScore = 49, Grade = "A", Remark = "x" },
                new GradeBoundaryDto { MinScore = 50, MaxScore = 100, Grade = "a", Remark = "y" }
            }
        };
        AppErrorException ex = await Assert.ThrowsAsync<AppErrorException>(
            () => CreateSut().SaveAsync(req, CancellationToken.None));
        Assert.Equal(400, ex.StatusCode);
    }

    [Theory]
    [InlineData(85, "A")]
    [InlineData(65, "B")]
    [InlineData(40, "D")]
    [InlineData(10, "F")]
    public void Resolve_MapsTotalToBand(int total, string expected)
    {
        (string grade, _) = GradingScale.Resolve(total, GradingScale.Defaults);
        Assert.Equal(expected, grade);
    }

    // Teachers enter half-marks, so totals like 59.5 fall between the integer band edges
    // (…-59 | 60-…). A decimal total must land in the band below the crack, never on "-".
    [Theory]
    [InlineData(69.5, "B")]
    [InlineData(59.5, "C")]
    [InlineData(39.5, "F")]
    [InlineData(0.5, "F")]
    public void Resolve_DecimalBetweenBands_FallsToLowerBand(double total, string expected)
    {
        (string grade, _) = GradingScale.Resolve((decimal)total, GradingScale.Defaults);
        Assert.Equal(expected, grade);
    }
}
