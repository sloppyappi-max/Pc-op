namespace FamilyHub.Server;

public static class EventTypes
{
    public const string Bill = "bill";
    public const string Paycheck = "paycheck";
    public const string Birthday = "birthday";
    public const string DayOff = "dayoff";
    public const string Other = "other";
}

public static class Recurrences
{
    public const string None = "none";
    public const string Weekly = "weekly";
    public const string Biweekly = "biweekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
}

/// <summary>Something the family puts on the calendar.</summary>
public class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Type { get; set; } = EventTypes.Other;
    public DateOnly Date { get; set; }
    /// <summary>Bill amount, or a fixed paycheck amount entered by hand.</summary>
    public decimal? Amount { get; set; }
    public string Recurrence { get; set; } = Recurrences.None;
    public string Notes { get; set; } = "";
    public string Person { get; set; } = "";

    // Day-off fields
    public string? JobId { get; set; }
    public bool Unpaid { get; set; }
    /// <summary>Hours missed. Null means the job's normal day length.</summary>
    public double? Hours { get; set; }
}

/// <summary>A job; its paychecks are estimated and shown on the calendar.</summary>
public class Job
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Person { get; set; } = "";
    /// <summary>"hourly" or "salary".</summary>
    public string PayType { get; set; } = "hourly";
    /// <summary>Hourly rate, or yearly salary.</summary>
    public decimal Rate { get; set; }
    public double HoursPerWeek { get; set; } = 40;
    public double HoursPerDay { get; set; } = 8;
    /// <summary>weekly, biweekly, semimonthly (15th + last day), monthly.</summary>
    public string Frequency { get; set; } = "biweekly";
    /// <summary>Any one real pay date; the schedule is worked out from it.</summary>
    public DateOnly AnchorPayDate { get; set; }
    /// <summary>Estimated share of gross taken by taxes, in percent.</summary>
    public decimal TaxPercent { get; set; } = 20;
    /// <summary>Fixed deductions per check (insurance, 401k, ...).</summary>
    public decimal DeductionsPerCheck { get; set; }
}

public class HubSettings
{
    public string FamilyName { get; set; } = "Our Family";
    /// <summary>Current bank balance, typed in by the user.</summary>
    public decimal Balance { get; set; }
    /// <summary>The day the balance was entered. Only events after it count toward the forecast.</summary>
    public DateOnly BalanceAsOf { get; set; } = DateOnly.FromDateTime(DateTime.Today);
    /// <summary>PIN phones must enter. The PC itself does not need it.</summary>
    public string Pin { get; set; } = Random.Shared.Next(100000, 1000000).ToString();
}

public class HubData
{
    public HubSettings Settings { get; set; } = new();
    public List<CalendarEvent> Events { get; set; } = new();
    public List<Job> Jobs { get; set; } = new();
}

/// <summary>One concrete appearance of an event (or an estimated paycheck) on a given day.</summary>
public record Occurrence(
    string SourceId,
    string Source,          // "event" or "job"
    string Title,
    string Type,
    DateOnly Date,
    decimal? Amount,
    string Person,
    string Notes,
    PaycheckBreakdown? Paycheck);

public record PaycheckBreakdown(
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    double HoursPaid,
    double UnpaidHoursOff,
    decimal Gross,
    decimal Taxes,
    decimal Deductions,
    decimal Net);
