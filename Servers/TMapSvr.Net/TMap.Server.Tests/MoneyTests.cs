using TMap.Server.Map;
using Xunit;

namespace TMap.Server.Tests;

/// <summary>Phase 10 — the money currency core (C++ CalcMoney / CTPlayer::UseMoney / EarnMoney): the
/// gold/silver/copper tiers combine/split on base 1000, and the two-phase spend.</summary>
public class MoneyTests
{
    [Fact]
    public void MoneyTotal_CombinesTiers_Base1000()
    {
        var ch = new Character { Gold = 1, Silver = 2, Cooper = 3 };
        Assert.Equal(1_002_003L, ch.MoneyTotal); // 1·1000² + 2·1000 + 3
    }

    [Fact]
    public void SetMoneyTotal_SplitsBackIntoTiers()
    {
        var ch = new Character();
        ch.SetMoneyTotal(1_002_003L);
        Assert.Equal(1u, ch.Gold);
        Assert.Equal(2u, ch.Silver);
        Assert.Equal(3u, ch.Cooper);
    }

    [Fact]
    public void UseMoney_CheckOnly_DoesNotDeduct()
    {
        var ch = new Character { Cooper = 100 };
        Assert.True(ch.UseMoney(30, commit: false));
        Assert.Equal(100u, ch.Cooper); // unchanged
    }

    [Fact]
    public void UseMoney_Commit_Deducts()
    {
        var ch = new Character { Cooper = 100 };
        Assert.True(ch.UseMoney(30, commit: true));
        Assert.Equal(70L, ch.MoneyTotal);
    }

    [Fact]
    public void UseMoney_Insufficient_ReturnsFalse_NoDeduct()
    {
        var ch = new Character { Cooper = 100 };
        Assert.False(ch.UseMoney(200, commit: true));
        Assert.Equal(100L, ch.MoneyTotal);
    }

    [Fact]
    public void UseMoney_Zero_AlwaysTrue()
    {
        var ch = new Character();
        Assert.True(ch.UseMoney(0, commit: true));
    }

    [Fact]
    public void UseMoney_SpendsAcrossTiers_WithBorrow()
    {
        var ch = new Character { Gold = 1 }; // total 1,000,000
        Assert.True(ch.UseMoney(1, commit: true));
        Assert.Equal(0u, ch.Gold);
        Assert.Equal(999u, ch.Silver);
        Assert.Equal(999u, ch.Cooper); // 999,999
    }

    [Fact]
    public void EarnMoney_Adds_AndCarriesTiers()
    {
        var ch = new Character();
        Assert.True(ch.EarnMoney(1500));
        Assert.Equal(1u, ch.Silver);
        Assert.Equal(500u, ch.Cooper);
    }

    [Fact]
    public void EarnMoney_Zero_ReturnsFalse()
    {
        var ch = new Character();
        Assert.False(ch.EarnMoney(0));
    }
}
