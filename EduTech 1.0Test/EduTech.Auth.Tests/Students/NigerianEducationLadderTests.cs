using EduTech.Shared.Constants;
using EduTech.Students.Classes.Domain;

namespace EduTech.Auth.Tests.Students;

public class NigerianEducationLadderTests
{
    [Fact]
    public void Ladder_Is_6_3_3_PlusNursery()
    {
        Assert.Equal(14, NigerianEducationLadder.All.Count);
        Assert.Equal(6, NigerianEducationLadder.All.Count(g => g.Stage == ClassLevel.Primary));
        Assert.Equal(3, NigerianEducationLadder.All.Count(g => g.Stage == ClassLevel.JuniorSecondary));
        Assert.Equal(3, NigerianEducationLadder.All.Count(g => g.Stage == ClassLevel.SeniorSecondary));
    }

    [Fact]
    public void NextGrade_CrossesStages()
    {
        NigerianEducationLadder.TryGetByName("Primary 6", out StandardGrade p6);
        Assert.Equal("JSS 1", NigerianEducationLadder.NextGrade(p6)!.Name);

        NigerianEducationLadder.TryGetByName("JSS 3", out StandardGrade jss3);
        Assert.Equal("SSS 1", NigerianEducationLadder.NextGrade(jss3)!.Name);
    }

    [Fact]
    public void NextGrade_AtSSS3_IsNull_AndTerminal()
    {
        NigerianEducationLadder.TryGetByName("SSS 3", out StandardGrade sss3);
        Assert.Null(NigerianEducationLadder.NextGrade(sss3)); // graduation
        Assert.True(NigerianEducationLadder.IsTerminal(sss3));
    }

    [Theory]
    [InlineData("nursery", 2)]
    [InlineData("primary", 6)]
    [InlineData("secondary", 6)]   // JSS 1-3 + SSS 1-3
    [InlineData("combined", 14)]
    [InlineData("something-odd", 14)] // unknown → full ladder
    public void GradesForType_MatchesSchoolType(string type, int expectedCount)
    {
        Assert.Equal(expectedCount, NigerianEducationLadder.GradesForType(type).Count);
    }

    [Fact]
    public void TryGetByName_IsCaseInsensitive()
    {
        Assert.True(NigerianEducationLadder.TryGetByName("jss 2", out StandardGrade g));
        Assert.Equal("JSS 2", g.Name);
        Assert.False(NigerianEducationLadder.TryGetByName("Grade 5", out _));
    }
}
