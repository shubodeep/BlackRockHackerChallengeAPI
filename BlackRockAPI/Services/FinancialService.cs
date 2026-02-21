using BlackrockChallenge.Models;

namespace BlackrockChallenge.Services;

public class FinancialService
{
    private const double NPS_RATE = 0.0711;
    private const double INDEX_RATE = 0.1449;
    private const string DATETIME_FMT = "yyyy-MM-dd HH:mm:ss";

    private static readonly string[] DateFormats = new[]
    {
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm"
    };

    // ─── Date helpers ─────────────────────────────────────────────────────────
    public static DateTime ParseDt(string value)
    {
        value = value.Trim();
        foreach (var fmt in DateFormats)
        {
            if (DateTime.TryParseExact(value, fmt,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
                return dt;
        }
        throw new ArgumentException($"Cannot parse datetime: '{value}'");
    }

    public static string FmtDt(DateTime dt) => dt.ToString(DATETIME_FMT);

    // ─── Math helpers ─────────────────────────────────────────────────────────
    public static double Ceiling100(double amount)
    {
        if (amount % 100 == 0) return amount;
        return Math.Ceiling(amount / 100.0) * 100.0;
    }

    public static double RemanentFrom(double amount) => Ceiling100(amount) - amount;

    /// <summary>Simplified Indian income tax slab on annual income.</summary>
    public static double CalculateTax(double income)
    {
        return income switch
        {
            <= 700_000 => 0,
            <= 1_000_000 => (income - 700_000) * 0.10,
            <= 1_200_000 => 300_000 * 0.10 + (income - 1_000_000) * 0.15,
            <= 1_500_000 => 300_000 * 0.10 + 200_000 * 0.15 + (income - 1_200_000) * 0.20,
            _ => 300_000 * 0.10 + 200_000 * 0.15 + 300_000 * 0.20 + (income - 1_500_000) * 0.30
        };
    }

    public static double TaxBenefit(double invested, double annualIncome)
    {
        var deduction = Math.Min(invested, Math.Min(annualIncome * 0.10, 200_000));
        return Math.Max(0, CalculateTax(annualIncome) - CalculateTax(annualIncome - deduction));
    }

    public static double CompoundInterest(double principal, double rate, int years)
        => principal * Math.Pow(1 + rate, years);

    public static double InflationAdjust(double amount, double inflationPct, int years)
        => amount / Math.Pow(1 + inflationPct / 100.0, years);

    public static int InvestmentYears(int age)
    {
        var diff = 60 - age;
        return diff > 0 ? diff : 5;
    }

    // ─── Period rule application ──────────────────────────────────────────────

    /// <summary>
    /// Q rule: replace remanent with fixed amount from the latest-starting
    /// matching period. On same start date, first in list wins.
    /// Caches parsed dates to avoid repeated parsing.
    /// </summary>
    public static double ApplyQRules(DateTime txDt, double baseRemanent, IList<QPeriod> qPeriods)
    {
        if (qPeriods.Count == 0)
            return baseRemanent;

        var matching = qPeriods
            .Select((q, idx) => new { q, idx, s = ParseDt(q.Start), e = ParseDt(q.End) })
            .Where(x => x.s <= txDt && txDt <= x.e)
            .OrderByDescending(x => x.s)
            .ThenBy(x => x.idx)
            .FirstOrDefault();

        return matching is null ? baseRemanent : matching.q.Fixed;
    }

    /// <summary>P rule: add all matching extra amounts to the remanent.
    /// Caches parsed dates to avoid repeated parsing.</summary>
    public static double ApplyPRules(DateTime txDt, double currentRemanent, IList<PPeriod> pPeriods)
    {
        if (pPeriods.Count == 0)
            return currentRemanent;

        var extra = pPeriods
            .Where(p => ParseDt(p.Start) <= txDt && txDt <= ParseDt(p.End))
            .Sum(p => p.Extra);
        return currentRemanent + extra;
    }

    /// <summary>Apply q then p rules, returning final remanent for each transaction.
    /// Caches all period dates upfront to avoid repeated parsing.</summary>
    public static List<(DateTime Dt, double Amount, double Ceiling, double Remanent)>
        ProcessWithPeriods(IEnumerable<ExpenseRequest> expenses, IList<QPeriod> q, IList<PPeriod> p)
    {
        // Cache parsed period dates upfront to avoid repeated parsing in loops
        var qCache = q.Select(x => (q: x, start: ParseDt(x.Start), end: ParseDt(x.End))).ToList();
        var pCache = p.Select(x => (p: x, start: ParseDt(x.Start), end: ParseDt(x.End))).ToList();

        var result = new List<(DateTime, double, double, double)>();
        foreach (var exp in expenses)
        {
            var dt = ParseDt(exp.Date);
            var ceil = Ceiling100(exp.Amount);
            var baseRem = ceil - exp.Amount;

            // Apply Q rules using cached dates
            var matching = qCache
                .Select((item, idx) => new { item.q, idx, item.start, item.end })
                .Where(x => x.start <= dt && dt <= x.end)
                .OrderByDescending(x => x.start)
                .ThenBy(x => x.idx)
                .FirstOrDefault();
            var rem = matching is null ? baseRem : matching.q.Fixed;

            // Apply P rules using cached dates
            var extra = pCache
                .Where(item => item.start <= dt && dt <= item.end)
                .Sum(item => item.p.Extra);
            rem += extra;

            result.Add((dt, exp.Amount, ceil, rem));
        }
        return result;
    }

    /// <summary>Sum remanents for transactions within a K period.
    /// Caches period dates to avoid repeated parsing.</summary>
    public static double SumKPeriod(
        IEnumerable<(DateTime Dt, double Amount, double Ceiling, double Remanent)> processed,
        KPeriod k)
    {
        var s = ParseDt(k.Start);
        var e = ParseDt(k.End);
        return processed
            .Where(tx => s <= tx.Dt && tx.Dt <= e)
            .Sum(tx => tx.Remanent);
    }

    // ─── Endpoint implementations ─────────────────────────────────────────────

    public ParseResponse ParseTransactions(IList<ExpenseRequest> expenses)
    {
        var txs = new List<TransactionResult>();
        double totalAmt = 0, totalCeil = 0, totalRem = 0;

        foreach (var exp in expenses)
        {
            var dt = ParseDt(exp.Date);
            var ceil = Ceiling100(exp.Amount);
            var rem = ceil - exp.Amount;

            txs.Add(new TransactionResult(FmtDt(dt), exp.Amount, ceil, rem));
            totalAmt += exp.Amount;
            totalCeil += ceil;
            totalRem += rem;
        }

        return new ParseResponse(txs, Round(totalAmt), Round(totalCeil), Round(totalRem));
    }

    public ValidatorResponse ValidateTransactions(ValidatorRequest req)
    {
        var valid = new List<TransactionResult>();
        var invalid = new List<InvalidTransaction>();
        var seen = new HashSet<string>();

        foreach (var tx in req.Transactions)
        {
            DateTime dt;
            try { dt = ParseDt(tx.Date); }
            catch { invalid.Add(new(tx.Date, tx.Amount, tx.Ceiling, tx.Remanent, "Invalid date format")); continue; }

            var dateKey = FmtDt(dt);
            var errors = new List<string>();

            if (tx.Amount < 0)
                errors.Add("Negative amounts are not allowed");

            if (tx.Amount >= 500_000)
                errors.Add("Amount exceeds maximum allowed transaction value");

            if (tx.Amount >= 0)
            {
                var expectedCeil = Ceiling100(tx.Amount);
                if (Math.Abs(tx.Ceiling - expectedCeil) > 0.01)
                    errors.Add($"Ceiling mismatch: expected {expectedCeil}, got {tx.Ceiling}");

                var expectedRem = expectedCeil - tx.Amount;
                if (Math.Abs(tx.Remanent - expectedRem) > 0.01)
                    errors.Add($"Remanent mismatch: expected {expectedRem}, got {tx.Remanent}");
            }

            if (errors.Count > 0)
            {
                invalid.Add(new(dateKey, tx.Amount, tx.Ceiling, tx.Remanent, string.Join("; ", errors)));
                continue;
            }

            if (seen.Contains(dateKey))
            {
                invalid.Add(new(dateKey, tx.Amount, tx.Ceiling, tx.Remanent, "Duplicate transaction"));
                continue;
            }

            seen.Add(dateKey);
            valid.Add(new(dateKey, tx.Amount, tx.Ceiling, tx.Remanent));
        }

        return new ValidatorResponse(valid, invalid);
    }

    public FilterResponse FilterTransactions(FilterRequest req)
    {
        var valid = new List<FilteredTransaction>();
        var invalid = new List<FilteredInvalidTransaction>();
        var seen = new HashSet<string>();

        // Cache K period dates upfront to avoid repeated parsing
        var kRanges = req.K.Select(k => (start: ParseDt(k.Start), end: ParseDt(k.End))).ToList();

        // Cache Q and P period dates for rule application
        var qCache = req.Q.Select(x => (q: x, start: ParseDt(x.Start), end: ParseDt(x.End))).ToList();
        var pCache = req.P.Select(x => (p: x, start: ParseDt(x.Start), end: ParseDt(x.End))).ToList();

        foreach (var tx in req.Transactions)
        {
            DateTime dt;
            try { dt = ParseDt(tx.Date); }
            catch { invalid.Add(new(tx.Date, tx.Amount, "Invalid date format")); continue; }

            var dateKey = FmtDt(dt);

            if (tx.Amount < 0)
            {
                invalid.Add(new(dateKey, tx.Amount, "Negative amounts are not allowed"));
                continue;
            }

            if (seen.Contains(dateKey))
            {
                invalid.Add(new(dateKey, tx.Amount, "Duplicate transaction"));
                continue;
            }

            seen.Add(dateKey);

            var ceil = Ceiling100(tx.Amount);
            var baseRem = ceil - tx.Amount;

            // Apply Q rules using cached dates
            var qMatching = qCache
                .Select((item, idx) => new { item.q, idx, item.start, item.end })
                .Where(x => x.start <= dt && dt <= x.end)
                .OrderByDescending(x => x.start)
                .ThenBy(x => x.idx)
                .FirstOrDefault();
            var rem = qMatching is null ? baseRem : qMatching.q.Fixed;

            // Apply P rules using cached dates
            var pExtra = pCache
                .Where(item => item.start <= dt && dt <= item.end)
                .Sum(item => item.p.Extra);
            rem += pExtra;

            // Check K periods using cached dates
            var inK = kRanges.Any(r => r.start <= dt && dt <= r.end);

            valid.Add(new(dateKey, tx.Amount, ceil, Round(rem), inK));
        }

        return new FilterResponse(valid, invalid);
    }

    public ReturnsResponse CalculateReturns(ReturnsRequest req, InvestmentType type)
    {
        var rate = type == InvestmentType.NPS ? NPS_RATE : INDEX_RATE;
        var annualIncome = req.Wage * 12;
        var years = InvestmentYears(req.Age);

        // Deduplicate and filter valid transactions
        var seen = new HashSet<string>();
        var validTxs = new List<ExpenseRequest>();
        foreach (var tx in req.Transactions)
        {
            if (tx.Amount < 0) continue;
            DateTime dt;
            try { dt = ParseDt(tx.Date); } catch { continue; }
            var key = FmtDt(dt);
            if (seen.Contains(key)) continue;
            seen.Add(key);
            validTxs.Add(new ExpenseRequest(FmtDt(dt), tx.Amount));
        }

        var totalAmount = validTxs.Sum(t => t.Amount);
        var totalCeiling = validTxs.Sum(t => Ceiling100(t.Amount));

        var processed = ProcessWithPeriods(validTxs, req.Q, req.P);

        // Cache K period dates upfront
        var kCache = req.K.Select(k => (k, start: ParseDt(k.Start), end: ParseDt(k.End))).ToList();

        var savingsByDates = kCache.Select(item =>
        {
            var invested = processed
                .Where(tx => item.start <= tx.Dt && tx.Dt <= item.end)
                .Sum(tx => tx.Remanent);
            var grossFuture = CompoundInterest(invested, rate, years);
            var realFuture = InflationAdjust(grossFuture, req.Inflation, years);
            var profit = Round(realFuture - invested);
            var taxBenefit = type == InvestmentType.NPS ? Round(TaxBenefit(invested, annualIncome)) : 0.0;

            return new SavingsByDate(
                FmtDt(item.start),
                FmtDt(item.end),
                Round(invested),
                profit,
                taxBenefit
            );
        }).ToList();

        return new ReturnsResponse(Round(totalAmount), Round(totalCeiling), savingsByDates);
    }

    private static double Round(double v) => Math.Round(v, 2);
}