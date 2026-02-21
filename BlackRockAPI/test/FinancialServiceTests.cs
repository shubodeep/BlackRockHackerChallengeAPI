// Test type: Unit Tests
// Validation: FinancialService business logic — parsing, validation, filtering, returns
// Command: dotnet test test/BlackrockChallenge.Tests.csproj

using BlackrockChallenge.Models;
using BlackrockChallenge.Services;
using Xunit;

namespace BlackrockChallenge.Tests;

public class FinancialServiceTests
{
    private readonly FinancialService _svc = new();

    // ─── Ceiling & Remanent ──────────────────────────────────────────────────
    [Theory]
    [InlineData(250, 300, 50)]
    [InlineData(375, 400, 25)]
    [InlineData(620, 700, 80)]
    [InlineData(480, 500, 20)]
    [InlineData(1519, 1600, 81)]
    [InlineData(500, 500, 0)]   // exact multiple
    [InlineData(100, 100, 0)]
    [InlineData(101, 200, 99)]
    public void Ceiling100_And_Remanent_AreCorrect(double amount, double expectedCeiling, double expectedRem)
    {
        Assert.Equal(expectedCeiling, FinancialService.Ceiling100(amount));
        Assert.Equal(expectedRem, FinancialService.RemanentFrom(amount));
    }

    // ─── Parse Transactions ───────────────────────────────────────────────────
    [Fact]
    public void ParseTransactions_Returns_Correct_Ceiling_And_Remanent()
    {
        var expenses = new List<ExpenseRequest>
        {
            new("2023-10-12 20:15:30", 250),
            new("2023-02-28 15:49:20", 375),
            new("2023-07-01 21:59:00", 620),
            new("2023-12-17 08:09:45", 480),
        };

        var result = _svc.ParseTransactions(expenses);

        Assert.Equal(4, result.Transactions.Count);
        Assert.Equal(50, result.Transactions[0].Remanent);
        Assert.Equal(300, result.Transactions[0].Ceiling);
        Assert.Equal(25, result.Transactions[1].Remanent);
        Assert.Equal(80, result.Transactions[2].Remanent);
        Assert.Equal(20, result.Transactions[3].Remanent);
        Assert.Equal(175, result.TotalRemanent);
        Assert.Equal(1725, result.TotalAmount);
    }

    // ─── Q Period Rules ───────────────────────────────────────────────────────
    [Fact]
    public void QRule_ReplacesRemanent_WithFixed()
    {
        var dt = new DateTime(2023, 7, 1, 21, 59, 0);
        var qPeriods = new List<QPeriod>
        {
            new QPeriod(0, "2023-07-01 00:00:00", "2023-07-31 23:59:00")
        };

        var result = FinancialService.ApplyQRules(dt, 80, qPeriods);
        Assert.Equal(0, result);
    }

    [Fact]
    public void QRule_NoMatch_ReturnsOriginalRemanent()
    {
        var dt = new DateTime(2023, 2, 28, 15, 49, 0);
        var qPeriods = new List<QPeriod>
        {
            new QPeriod(0, "2023-07-01 00:00:00", "2023-07-31 23:59:00")
        };

        var result = FinancialService.ApplyQRules(dt, 25, qPeriods);
        Assert.Equal(25, result);
    }

    [Fact]
    public void QRule_MultipleMatch_UsesLatestStart()
    {
        var dt = new DateTime(2023, 7, 15, 0, 0, 0);
        var qPeriods = new List<QPeriod>
        {
            new QPeriod(10, "2023-07-01 00:00:00", "2023-07-31 23:59:00"),
            new QPeriod(99, "2023-07-10 00:00:00", "2023-07-20 23:59:00"),  // later start → wins
        };

        var result = FinancialService.ApplyQRules(dt, 80, qPeriods);
        Assert.Equal(99, result);
    }

    [Fact]
    public void QRule_SameStart_FirstInListWins()
    {
        var dt = new DateTime(2023, 7, 15, 0, 0, 0);
        var qPeriods = new List<QPeriod>
        {
            new QPeriod(42, "2023-07-10 00:00:00", "2023-07-31 23:59:00"),  // first → wins
            new QPeriod(99, "2023-07-10 00:00:00", "2023-07-31 23:59:00"),
        };

        var result = FinancialService.ApplyQRules(dt, 80, qPeriods);
        Assert.Equal(42, result);
    }

    // ─── P Period Rules ───────────────────────────────────────────────────────
    [Fact]
    public void PRule_AddsExtra_ToRemanent()
    {
        var dt = new DateTime(2023, 10, 12, 20, 15, 0);
        var pPeriods = new List<PPeriod>
        {
            new PPeriod(25, "2023-10-01 08:00:00", "2023-12-31 19:59:00")
        };

        var result = FinancialService.ApplyPRules(dt, 50, pPeriods);
        Assert.Equal(75, result);
    }

    [Fact]
    public void PRule_MultipleMatching_SumsAllExtras()
    {
        var dt = new DateTime(2023, 11, 15, 0, 0, 0);
        var pPeriods = new List<PPeriod>
        {
            new PPeriod(25, "2023-10-01 00:00:00", "2023-12-31 23:59:00"),
            new PPeriod(10, "2023-11-01 00:00:00", "2023-11-30 23:59:00"),
        };

        var result = FinancialService.ApplyPRules(dt, 50, pPeriods);
        Assert.Equal(85, result); // 50 + 25 + 10
    }

    // ─── Full Example from Spec ────────────────────────────────────────────────
    [Fact]
    public void FullExample_Spec_KPeriods_MatchExpected()
    {
        var expenses = new List<ExpenseRequest>
        {
            new("2023-10-12 20:15:00", 250),
            new("2023-02-28 15:49:00", 375),
            new("2023-07-01 21:59:00", 620),
            new("2023-12-17 08:09:00", 480),
        };

        var q = new List<QPeriod> { new(0, "2023-07-01 00:00:00", "2023-07-31 23:59:00") };
        var p = new List<PPeriod> { new(25, "2023-10-01 08:00:00", "2023-12-31 19:59:00") };
        var k = new List<KPeriod>
        {
            new("2023-03-01 00:00:00", "2023-11-30 23:59:00"),
            new("2023-01-01 00:00:00", "2023-12-31 23:59:00"),
        };

        var processed = FinancialService.ProcessWithPeriods(expenses, q, p);

        var sum1 = FinancialService.SumKPeriod(processed, k[0]);
        var sum2 = FinancialService.SumKPeriod(processed, k[1]);

        Assert.Equal(75, sum1);   // spec: March–Nov = 75
        Assert.Equal(145, sum2);  // spec: full year = 145
    }

    // ─── Validator: Negative Amount ───────────────────────────────────────────
    [Fact]
    public void Validator_RejectsNegativeAmounts()
    {
        var req = new ValidatorRequest(50000, new List<TransactionResult>
        {
            new("2023-07-10 09:15:00", -250, 200, 30),
        });

        var result = _svc.ValidateTransactions(req);

        Assert.Empty(result.Valid);
        Assert.Single(result.Invalid);
        Assert.Contains("Negative", result.Invalid[0].Message);
    }

    [Fact]
    public void Validator_RejectsDuplicateDates()
    {
        var req = new ValidatorRequest(50000, new List<TransactionResult>
        {
            new("2023-10-12 20:15:30", 250, 300, 50),
            new("2023-10-12 20:15:30", 250, 300, 50),
        });

        var result = _svc.ValidateTransactions(req);

        Assert.Single(result.Valid);
        Assert.Single(result.Invalid);
        Assert.Contains("Duplicate", result.Invalid[0].Message);
    }

    // ─── Tax calculation ──────────────────────────────────────────────────────
    [Theory]
    [InlineData(600_000, 0)]           // below 7L → 0%
    [InlineData(700_000, 0)]           // exactly 7L → 0%
    [InlineData(850_000, 15_000)]      // 7L–10L bracket: (850k-700k)*10%
    [InlineData(1_100_000, 45_000)]    // 30k + 15k = 45k
    public void TaxCalculation_IsCorrect(double income, double expectedTax)
    {
        Assert.Equal(expectedTax, FinancialService.CalculateTax(income));
    }

    [Fact]
    public void TaxBenefit_BelowTaxSlab_IsZero()
    {
        // annual income 600k → 0% slab, so tax benefit = 0
        var benefit = FinancialService.TaxBenefit(145, 600_000);
        Assert.Equal(0, benefit);
    }

    // ─── Compound interest ────────────────────────────────────────────────────
    [Fact]
    public void CompoundInterest_NPS_MatchesSpec()
    {
        // Spec: 145 × (1.0711)^31 ≈ 1219.45
        var result = FinancialService.CompoundInterest(145, 0.0711, 31);
        Assert.InRange(result, 1210, 1230);
    }

    [Fact]
    public void CompoundInterest_Index_MatchesSpec()
    {
        // Spec: 145 × (1.1449)^31 ≈ 9619.7
        var result = FinancialService.CompoundInterest(145, 0.1449, 31);
        Assert.InRange(result, 9600, 9650);
    }

    // ─── Inflation adjustment ─────────────────────────────────────────────────
    [Fact]
    public void InflationAdjust_Index_MatchesSpec()
    {
        // Spec: 9619.7 / (1.055)^31 ≈ 1829.5
        var gross = FinancialService.CompoundInterest(145, 0.1449, 31);
        var real = FinancialService.InflationAdjust(gross, 5.5, 31);
        Assert.InRange(real, 1820, 1840);
    }

    // ─── Investment years ─────────────────────────────────────────────────────
    [Theory]
    [InlineData(29, 31)]
    [InlineData(59, 1)]
    [InlineData(60, 5)]   // age ≥ 60 → minimum 5
    [InlineData(65, 5)]
    public void InvestmentYears_IsCorrect(int age, int expected)
    {
        Assert.Equal(expected, FinancialService.InvestmentYears(age));
    }

    // ─── Returns endpoint full flow ───────────────────────────────────────────
    [Fact]
    public void Returns_Index_FullYear_MatchesSpec()
    {
        var req = new ReturnsRequest(
            Age: 29,
            Wage: 50000,
            Inflation: 5.5,
            Q: [new(0, "2023-07-01 00:00:00", "2023-07-31 23:59:00")],
            P: [new(25, "2023-10-01 08:00:00", "2023-12-31 19:59:00")],
            K: [new("2023-01-01 00:00:00", "2023-12-31 23:59:00")],
            Transactions: [
                new("2023-10-12 20:15:00", 250),
                new("2023-02-28 15:49:00", 375),
                new("2023-07-01 21:59:00", 620),
                new("2023-12-17 08:09:00", 480),
            ]
        );

        var result = _svc.CalculateReturns(req, InvestmentType.Index);

        Assert.Equal(145, result.SavingsByDates[0].Amount);
        Assert.InRange(result.SavingsByDates[0].Profit, 1680, 1690); // real return - principal ≈ 1684
    }
}