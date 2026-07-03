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
            Bands = new() { new GradeBoundaryDto { MinScore = 50, MaxScore = 100, Grade = "Pass", Remark = "OK" } }
        };
        await CreateSut().SaveAsync(req, CancellationToken.None);
        _repo.Verify(r => r.ReplaceAsync(It.Is<IReadOnlyList<GradeBoundaryDto>>(b => b.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
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
}
