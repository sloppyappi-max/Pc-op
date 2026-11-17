# Family Hub

A family life manager for your Windows PC. The main screen is a calendar of the current month,
with a money panel beside it. Anyone in the house can add things from their phone over your home Wi-Fi.

## What it does

- **Calendar**: bills, paychecks, birthdays, days off and anything else, with repeats
  (weekly, every 2 weeks, monthly, yearly).
- **Phone access**: click **Connect phone** on the PC, scan the QR code on your phone, and enter the PIN.
  Use "Add to Home Screen" on the phone so it opens like an app. Works on the same Wi-Fi as the PC.
- **Paycheck estimates**: add each job (hourly or salary, pay schedule, tax %, deductions).
  Paydays appear on the calendar with estimated take-home pay. Mark a **Day off** as *unpaid* and
  that check goes down automatically. Click a payday to see the breakdown.
- **Money forecast**: enter your bank balance and Family Hub shows this month's paychecks and bills,
  what's left after bills, your balance at month end, and warns you if you're headed below zero.

## Running it

**On Windows (the real app):** download the `FamilyHub-windows-x64` build from the repo's
**Actions** tab, unzip it and run `FamilyHub.exe`. The first time, Windows Firewall asks about network
access: allow it on **private networks** so phones can connect.
It needs the Microsoft Edge WebView2 Runtime, which comes with Windows 10/11.

**For development (any OS):**

```
dotnet run --project src/FamilyHub.Server
```

Then open http://localhost:5180.

Your data lives in `%AppData%\FamilyHub\familyhub.json`. Back up that file to keep a copy.

## Project layout

| Folder | What it is |
| --- | --- |
| `src/FamilyHub.Server` | Local web server: data storage, calendar, paycheck and budget math, phone PIN check, and the UI (`wwwroot`) |
| `src/FamilyHub.Desktop` | The Windows window (WPF + WebView2) that starts the server and shows the calendar |

## Security

- The PC itself doesn't need the PIN. Every other device does.
- Click **Connect phone → Make a new PIN** to lock out phones that already connected.
- It only works on your home network. It isn't reachable from the internet.

## Not built yet

- **Bank connection.** Linking a bank automatically needs a service like Plaid (developer account
  + API keys, paid in production). For now you type in your balance. The forecast is set up so a bank
  feed can replace that later.
- **Payroll app connections.** Most payroll providers (ADP, Gusto, Paychex, ...) have no public API
  for employees, so paychecks are estimated from each job's details instead. Possible next step:
  importing pay stubs (PDF/CSV) to fine-tune the estimates.
