namespace FamilyHub.Server;

/// <summary>Calendar, paycheck and budget math. Pure functions, no I/O.</summary>
public static class Planner
{
    // ---------- Recurring events ----------

    public static IEnumerable<DateOnly> ExpandDates(DateOnly start, string recurrence, DateOnly from, DateOnly to)
    {
        switch (recurrence)
        {
            case Recurrences.Weekly:
            case Recurrences.Biweekly:
            {
                int step = recurrence == Recurrences.Weekly ? 7 : 14;
                var d = start;
                if (d < from)
                {
                    int skip = (from.DayNumber - d.DayNumber) / step;
                    d = d.AddDays(skip * step);
                }
                for (; d <= to; d = d.AddDays(step))
                    if (d >= from && d >= start) yield return d;
                break;
            }
            case Recurrences.Monthly:
                for (var m = new DateOnly(Math.Max(from.Year, start.Year), 1, 1); m <= to; m = m.AddMonths(1))
                {
                    var d = ClampDay(m.Year, m.Month, start.Day);
                    if (d >= start && d >= from && d <= to) yield return d;
                }
                break;
            case Recurrences.Yearly:
                for (int y = Math.Max(from.Year, start.Year); y <= to.Year; y++)
                {
                    var d = ClampDay(y, start.Month, start.Day);
                    if (d >= start && d >= from && d <= to) yield return d;
                }
                break;
            default:
                if (start >= from && start <= to) yield return start;
                break;
        }
    }

    private static DateOnly ClampDay(int year, int month, int day) =>
        new(year, month, Math.Min(day, DateTime.DaysInMonth(year, month)));

    // ---------- Paychecks ----------

    public static int PeriodsPerYear(string frequency) => frequency switch
    {
        "weekly" => 52,
        "biweekly" => 26,
        "semimonthly" => 24,
        "monthly" => 12,
        _ => 26,
    };

    /// <summary>Pay dates for a job between from and to (inclusive).</summary>
    public static IEnumerable<DateOnly> PayDates(Job job, DateOnly from, DateOnly to)
    {
        switch (job.Frequency)
        {
            case "weekly":
            case "biweekly":
            {
                int step = job.Frequency == "weekly" ? 7 : 14;
                // Step from the anchor in either direction so past months show paychecks too.
                int offset = (int)Math.Floor((from.DayNumber - job.AnchorPayDate.DayNumber) / (double)step);
                for (var d = job.AnchorPayDate.AddDays(offset * step); d <= to; d = d.AddDays(step))
                    if (d >= from) yield return d;
                break;
            }
            case "semimonthly":
                for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                {
                    foreach (var d in new[] { new DateOnly(m.Year, m.Month, 15), ClampDay(m.Year, m.Month, 31) })
                        if (d >= from && d <= to) yield return d;
                }
                break;
            case "monthly":
                for (var m = new DateOnly(from.Year, from.Month, 1); m <= to; m = m.AddMonths(1))
                {
                    var d = ClampDay(m.Year, m.Month, job.AnchorPayDate.Day);
                    if (d >= from && d <= to) yield return d;
                }
                break;
        }
    }

    /// <summary>The pay date before the given one; together they bound the pay period.</summary>
    public static DateOnly PreviousPayDate(Job job, DateOnly payDate)
    {
        return job.Frequency switch
        {
            "weekly" => payDate.AddDays(-7),
            "biweekly" => payDate.AddDays(-14),
            "semimonthly" => payDate.Day == 15
                ? ClampDay(payDate.AddMonths(-1).Year, payDate.AddMonths(-1).Month, 31)
                : new DateOnly(payDate.Year, payDate.Month, 15),
            _ => ClampDay(payDate.AddMonths(-1).Year, payDate.AddMonths(-1).Month, job.AnchorPayDate.Day),
        };
    }

    /// <summary>
    /// Estimate one paycheck. The pay period is the days after the previous pay date up to and
    /// including this one. Unpaid days off in that window are taken out of the pay.
    /// </summary>
    public static PaycheckBreakdown EstimatePaycheck(Job job, DateOnly payDate, IEnumerable<CalendarEvent> events)
    {
        var periodStart = PreviousPayDate(job, payDate).AddDays(1);
        var periodEnd = payDate;
        int periodDays = periodEnd.DayNumber - periodStart.DayNumber + 1;

        double unpaidHours = events
            .Where(e => e.Type == EventTypes.DayOff && e.Unpaid && e.JobId == job.Id)
            .SelectMany(e => ExpandDates(e.Date, e.Recurrence, periodStart, periodEnd)
                .Select(_ => e.Hours ?? job.HoursPerDay))
            .Sum();

        decimal gross;
        double hoursPaid;
        if (job.PayType == "salary")
        {
            decimal perCheck = job.Rate / PeriodsPerYear(job.Frequency);
            decimal perHour = job.HoursPerWeek > 0 ? job.Rate / 52m / (decimal)job.HoursPerWeek : 0;
            gross = perCheck - perHour * (decimal)unpaidHours;
            hoursPaid = job.HoursPerWeek * 52 / PeriodsPerYear(job.Frequency) - unpaidHours;
        }
        else
        {
            double scheduled = job.HoursPerWeek * periodDays / 7.0;
            hoursPaid = Math.Max(0, scheduled - unpaidHours);
            gross = job.Rate * (decimal)hoursPaid;
        }

        gross = Math.Max(0, Math.Round(gross, 2));
        decimal taxes = Math.Round(gross * job.TaxPercent / 100m, 2);
        decimal deductions = Math.Min(job.DeductionsPerCheck, gross - taxes);
        decimal net = gross - taxes - deductions;

        return new PaycheckBreakdown(periodStart, periodEnd, Math.Round(Math.Max(0, hoursPaid), 2),
            unpaidHours, gross, taxes, deductions, net);
    }

    // ---------- Calendar ----------

    public static List<Occurrence> Occurrences(HubData data, DateOnly from, DateOnly to)
    {
        var list = new List<Occurrence>();

        foreach (var e in data.Events)
            foreach (var d in ExpandDates(e.Date, e.Recurrence, from, to))
                list.Add(new Occurrence(e.Id, "event", e.Title, e.Type, d, e.Amount, e.Person, e.Notes, null));

        foreach (var job in data.Jobs)
            foreach (var d in PayDates(job, from, to))
            {
                var pay = EstimatePaycheck(job, d, data.Events);
                var title = string.IsNullOrWhiteSpace(job.Person) ? $"{job.Name} pay" : $"{job.Person}'s pay ({job.Name})";
                list.Add(new Occurrence(job.Id, "job", title, EventTypes.Paycheck, d, pay.Net, job.Person, "Estimated", pay));
            }

        return list.OrderBy(o => o.Date).ThenBy(o => o.Type).ThenBy(o => o.Title).ToList();
    }

    // ---------- Money summary ----------

    public record LedgerLine(DateOnly Date, string Title, string Type, decimal Amount, decimal Balance);

    public record MonthSummary(
        int Year,
        int Month,
        decimal IncomeTotal,
        decimal BillsTotal,
        decimal LeftAfterBills,
        decimal StartingBalance,
        DateOnly BalanceAsOf,
        decimal? ProjectedEndBalance,
        decimal? LowestBalance,
        DateOnly? LowestBalanceDate,
        List<LedgerLine> Ledger);

    /// <summary>
    /// Totals for the month, plus a running balance that starts from the entered bank balance
    /// and adds paychecks / subtracts bills on each day after it.
    /// </summary>
    public static MonthSummary Summarize(HubData data, int year, int month)
    {
        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var s = data.Settings;

        var monthOcc = Occurrences(data, monthStart, monthEnd)
            .Where(o => o.Amount.HasValue && (o.Type == EventTypes.Bill || o.Type == EventTypes.Paycheck))
            .ToList();
        decimal income = monthOcc.Where(o => o.Type == EventTypes.Paycheck).Sum(o => o.Amount!.Value);
        decimal bills = monthOcc.Where(o => o.Type == EventTypes.Bill).Sum(o => o.Amount!.Value);

        var ledger = new List<LedgerLine>();
        decimal? endBalance = null, lowest = null;
        DateOnly? lowestDate = null;

        if (monthEnd > s.BalanceAsOf)
        {
            // Walk from the day after the balance was entered to the end of this month.
            decimal balance = s.Balance;
            lowest = balance;
            lowestDate = s.BalanceAsOf;
            var walk = Occurrences(data, s.BalanceAsOf.AddDays(1), monthEnd)
                .Where(o => o.Amount.HasValue && (o.Type == EventTypes.Bill || o.Type == EventTypes.Paycheck))
                .OrderBy(o => o.Date).ThenBy(o => o.Type == EventTypes.Paycheck ? 0 : 1);
            foreach (var o in walk)
            {
                decimal delta = o.Type == EventTypes.Paycheck ? o.Amount!.Value : -o.Amount!.Value;
                balance += delta;
                if (o.Date >= monthStart)
                    ledger.Add(new LedgerLine(o.Date, o.Title, o.Type, delta, balance));
                if (balance < lowest) { lowest = balance; lowestDate = o.Date; }
            }
            endBalance = balance;
        }

        return new MonthSummary(year, month, income, bills, income - bills, s.Balance, s.BalanceAsOf,
            endBalance, lowest, lowestDate, ledger);
    }
}
