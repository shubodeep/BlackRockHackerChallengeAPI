using System.Text.Json.Serialization;

namespace BlackrockChallenge.Models;

// ─── Enums ────────────────────────────────────────────────────────────────────
public enum InvestmentType { NPS, Index }

// ─── Shared date/period types ─────────────────────────────────────────────────
public record QPeriod(double Fixed, string Start, string End);
public record PPeriod(double Extra, string Start, string End);
public record KPeriod(string Start, string End);

// ─── Endpoint 1: Parse ────────────────────────────────────────────────────────
public record ExpenseRequest(string Date, double Amount);

public record TransactionResult(
    string Date,
    double Amount,
    double Ceiling,
    double Remanent
);

public record ParseResponse(
    List<TransactionResult> Transactions,
    double TotalAmount,
    double TotalCeiling,
    double TotalRemanent
);

// ─── Endpoint 2: Validator ────────────────────────────────────────────────────
public record ValidatorRequest(double Wage, List<TransactionResult> Transactions);

public record InvalidTransaction(
    string Date,
    double Amount,
    double Ceiling,
    double Remanent,
    string Message
);

public record ValidatorResponse(
    List<TransactionResult> Valid,
    List<InvalidTransaction> Invalid
);

// ─── Endpoint 3: Filter ───────────────────────────────────────────────────────
public record FilterTransactionInput(string Date, double Amount);

public record FilterRequest(
    List<QPeriod> Q,
    List<PPeriod> P,
    List<KPeriod> K,
    double Wage,
    List<FilterTransactionInput> Transactions
);

public record FilteredTransaction(
    string Date,
    double Amount,
    double Ceiling,
    double Remanent,
    bool InKPeriod
);

public record FilteredInvalidTransaction(
    string Date,
    double Amount,
    string Message
);

public record FilterResponse(
    List<FilteredTransaction> Valid,
    List<FilteredInvalidTransaction> Invalid
);

// ─── Endpoint 4: Returns ──────────────────────────────────────────────────────
public record ReturnsRequest(
    int Age,
    double Wage,
    double Inflation,
    List<QPeriod> Q,
    List<PPeriod> P,
    List<KPeriod> K,
    List<ExpenseRequest> Transactions
);

public record SavingsByDate(
    string Start,
    string End,
    double Amount,
    double Profit,
    double TaxBenefit
);

public record ReturnsResponse(
    double TotalTransactionAmount,
    double TotalCeiling,
    List<SavingsByDate> SavingsByDates
);