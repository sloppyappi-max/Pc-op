"use strict";

// ---------- Helpers ----------
const $ = (sel) => document.querySelector(sel);
const money = (n, opts = {}) =>
  (n ?? 0).toLocaleString(undefined, { style: "currency", currency: "USD", ...opts });
const pad = (n) => String(n).padStart(2, "0");
const iso = (d) => `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`;
const parseIso = (s) => { const [y, m, d] = s.split("-").map(Number); return new Date(y, m - 1, d); };
const esc = (s) => String(s ?? "").replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const TYPE_LABEL = { bill: "Bill", paycheck: "Paycheck", birthday: "Birthday", dayoff: "Day off", other: "Other" };
const REPEAT_LABEL = { none: "", weekly: "every week", biweekly: "every 2 weeks", monthly: "every month", yearly: "every year" };
const FREQ_LABEL = { weekly: "weekly", biweekly: "every 2 weeks", semimonthly: "15th & last day", monthly: "monthly" };

const state = {
  year: new Date().getFullYear(),
  month: new Date().getMonth(), // 0-based
  occurrences: [],
  events: [],
  jobs: [],
  settings: null,
  local: false,
};

// ---------- API ----------
async function api(path, opts = {}) {
  const headers = { "Content-Type": "application/json", ...(opts.headers || {}) };
  const pin = localStorage.getItem("familyhub.pin");
  if (pin) headers["X-Pin"] = pin;
  const res = await fetch(path, { ...opts, headers, body: opts.body ? JSON.stringify(opts.body) : undefined });
  if (res.status === 401) {
    await askForPin();
    return api(path, opts);
  }
  if (!res.ok) {
    let msg = `Something went wrong (${res.status})`;
    try { msg = (await res.json()).error || msg; } catch { /* not JSON */ }
    throw new Error(msg);
  }
  return res.status === 204 ? null : res.json();
}

let pinWaiter = null;
function askForPin() {
  if (pinWaiter) return pinWaiter;
  pinWaiter = new Promise((resolve) => {
    const dlg = $("#pinDialog");
    const form = $("#pinForm");
    $("#pinError").hidden = !localStorage.getItem("familyhub.pin");
    form.reset();
    dlg.addEventListener("cancel", (e) => e.preventDefault()); // must unlock to continue
    form.onsubmit = (e) => {
      e.preventDefault();
      localStorage.setItem("familyhub.pin", form.pin.value.trim());
      dlg.close();
      pinWaiter = null;
      resolve();
    };
    dlg.showModal();
  });
  return pinWaiter;
}

// ---------- Loading ----------
function visibleRange() {
  const first = new Date(state.year, state.month, 1);
  const start = new Date(first); start.setDate(1 - first.getDay());
  const end = new Date(start); end.setDate(start.getDate() + 41);
  return { start, end };
}

async function refresh() {
  const { start, end } = visibleRange();
  const [occ, summary, events, jobs, settings] = await Promise.all([
    api(`/api/calendar?from=${iso(start)}&to=${iso(end)}`),
    api(`/api/summary?year=${state.year}&month=${state.month + 1}`),
    api("/api/events"),
    api("/api/jobs"),
    api("/api/settings"),
  ]);
  Object.assign(state, { occurrences: occ, events, jobs, settings });
  renderCalendar();
  renderMoney(summary);
  $("#familyName").textContent = settings.familyName;
  document.title = `${settings.familyName} · Family Hub`;
}

// ---------- Calendar ----------
function renderCalendar() {
  const monthName = new Date(state.year, state.month, 1).toLocaleDateString(undefined, { month: "long", year: "numeric" });
  $("#monthTitle").textContent = monthName;
  const { start } = visibleRange();
  const today = iso(new Date());
  const byDay = {};
  for (const o of state.occurrences) (byDay[o.date] ||= []).push(o);

  const grid = $("#grid");
  grid.innerHTML = "";
  for (let i = 0; i < 42; i++) {
    const d = new Date(start); d.setDate(start.getDate() + i);
    const key = iso(d);
    const items = byDay[key] || [];
    const cell = document.createElement("div");
    cell.className = "day" + (d.getMonth() !== state.month ? " outside" : "") + (key === today ? " today" : "");
    cell.setAttribute("role", "button");
    cell.setAttribute("aria-label", `${d.toDateString()}, ${items.length} item(s)`);
    const max = 3;
    cell.innerHTML =
      `<div class="num">${d.getDate()}</div>` +
      items.slice(0, max).map((o, idx) =>
        `<div class="chip ${o.type}" data-idx="${idx}" title="${esc(o.title)}"><span class="t">${esc(o.title)}</span>` +
        (o.amount != null ? `<span class="a">${money(o.amount, { maximumFractionDigits: 0 })}</span>` : "") + `</div>`
      ).join("") +
      (items.length > max ? `<div class="more">+${items.length - max} more</div>` : "") +
      `<div class="dots">${items.slice(0, 4).map((o) => `<i class="${o.type}"></i>`).join("")}</div>`;
    cell.onclick = (e) => {
      const chip = e.target.closest(".chip");
      if (chip) openOccurrence(items[Number(chip.dataset.idx)]); // click an item: open it
      else openDay(key);                                         // click the day: list + add
    };
    grid.appendChild(cell);
  }
}

function openDay(key) {
  const items = state.occurrences.filter((o) => o.date === key);
  const d = parseIso(key);
  $("#dayTitle").textContent = d.toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" });
  const list = $("#dayList");
  list.innerHTML = items.length ? "" : `<li class="empty">Nothing planned.</li>`;
  for (const o of items) {
    const li = document.createElement("li");
    li.className = o.type;
    const sub = [TYPE_LABEL[o.type], o.person, o.notes].filter(Boolean).join(" · ");
    li.innerHTML = `<span class="bar"></span><div class="info"><div>${esc(o.title)}</div><div class="sub">${esc(sub)}</div></div>` +
      (o.amount != null ? `<span class="amt ${o.type === "bill" ? "neg" : o.type === "paycheck" ? "pos" : ""}">${money(o.amount)}</span>` : "");
    li.onclick = () => { $("#dayDialog").close(); openOccurrence(o); };
    list.appendChild(li);
  }
  $("#dayAdd").onclick = () => { $("#dayDialog").close(); openEventForm(null, key); };
  $("#dayDialog").showModal();
}

function openOccurrence(o) {
  if (o.source === "job") return openPaycheck(o);
  const ev = state.events.find((e) => e.id === o.sourceId);
  if (ev) openEventForm(ev);
}

// ---------- Money panel ----------
function renderMoney(s) {
  const st = state.settings;
  $("#balance").textContent = money(st.balance);
  $("#balanceAsOf").textContent = `as of ${parseIso(st.balanceAsOf).toLocaleDateString()}`;
  const monthName = new Date(s.year, s.month - 1, 1).toLocaleDateString(undefined, { month: "long" });
  $("#monthMoneyTitle").textContent = monthName;
  $("#incomeTotal").textContent = money(s.incomeTotal);
  $("#billsTotal").textContent = money(s.billsTotal);
  const left = $("#leftAfter");
  left.textContent = money(s.leftAfterBills);
  left.className = s.leftAfterBills < 0 ? "neg" : "pos";

  const fc = $("#forecastCard");
  if (s.projectedEndBalance == null) {
    $("#endBalance").textContent = "–";
    $("#lowest").textContent = "–";
    $("#lowWarn").hidden = true;
    fc.querySelector(".label").textContent = "Forecast (past month)";
  } else {
    fc.querySelector(".label").textContent = "Forecast";
    const eb = $("#endBalance");
    eb.textContent = money(s.projectedEndBalance);
    eb.className = s.projectedEndBalance < 0 ? "neg" : "";
    $("#lowest").textContent = `${money(s.lowestBalance)} on ${parseIso(s.lowestBalanceDate).toLocaleDateString(undefined, { month: "short", day: "numeric" })}`;
    const warn = $("#lowWarn");
    warn.hidden = s.lowestBalance >= 0;
    if (s.lowestBalance < 0) warn.textContent = `Heads up: you're projected to go ${money(-s.lowestBalance)} below zero around ${parseIso(s.lowestBalanceDate).toLocaleDateString(undefined, { month: "short", day: "numeric" })}.`;
  }

  const ledger = $("#ledger");
  if (!s.ledger.length) {
    ledger.innerHTML = `<li class="empty">${s.projectedEndBalance == null
      ? "This month is before your balance date."
      : "No bills or paychecks yet. Add a bill, or add a job under \"Jobs & pay\"."}</li>`;
    return;
  }
  ledger.innerHTML = s.ledger.map((l) => {
    const d = parseIso(l.date);
    return `<li><span class="d">${d.toLocaleDateString(undefined, { month: "short", day: "numeric" })}</span>` +
      `<span class="t" title="${esc(l.title)}">${esc(l.title)}</span>` +
      `<span class="r"><span class="${l.amount < 0 ? "neg" : "pos"}">${l.amount < 0 ? "−" : "+"}${money(Math.abs(l.amount))}</span>` +
      `<small>${money(l.balance)} left</small></span></li>`;
  }).join("");
}

// ---------- Event form ----------
function openEventForm(ev, dateKey) {
  const dlg = $("#eventDialog");
  const f = $("#eventForm");
  f.reset();
  $("#eventError").hidden = true;
  $("#eventDialogTitle").textContent = ev ? "Edit event" : "Add event";
  const jobSel = f.jobId;
  jobSel.innerHTML = `<option value="">— none —</option>` +
    state.jobs.map((j) => `<option value="${esc(j.id)}">${esc(j.name)}${j.person ? " (" + esc(j.person) + ")" : ""}</option>`).join("");

  const e = ev || { type: "bill", date: dateKey || iso(new Date()), recurrence: "none" };
  f.querySelector(`input[name=type][value=${e.type}]`).checked = true;
  f.title.value = e.title || "";
  f.date.value = e.date;
  f.recurrence.value = e.recurrence || "none";
  f.amount.value = e.amount ?? "";
  f.person.value = e.person || "";
  f.notes.value = e.notes || "";
  f.jobId.value = e.jobId || "";
  f.hours.value = e.hours ?? "";
  f.unpaid.checked = !!e.unpaid;
  if (!ev) {
    // Sensible repeat defaults for new items
    f.recurrence.value = "none";
  }
  syncTypeFields();

  const del = $("#deleteEvent");
  del.hidden = !ev;
  del.onclick = async () => {
    if (!confirm(`Delete "${ev.title}"${ev.recurrence !== "none" ? " and all its repeats" : ""}?`)) return;
    await api(`/api/events/${ev.id}`, { method: "DELETE" });
    dlg.close();
    refresh();
  };

  f.onsubmit = async (event) => {
    event.preventDefault();
    const type = f.querySelector("input[name=type]:checked").value;
    const body = {
      title: f.title.value.trim(),
      type,
      date: f.date.value,
      recurrence: f.recurrence.value,
      amount: (type === "bill" || type === "paycheck") && f.amount.value !== "" ? Number(f.amount.value) : null,
      person: f.person.value.trim(),
      notes: f.notes.value.trim(),
      jobId: type === "dayoff" && f.jobId.value ? f.jobId.value : null,
      unpaid: type === "dayoff" && f.unpaid.checked,
      hours: type === "dayoff" && f.hours.value !== "" ? Number(f.hours.value) : null,
    };
    try {
      if (ev) await api(`/api/events/${ev.id}`, { method: "PUT", body });
      else await api("/api/events", { method: "POST", body });
      dlg.close();
      refresh();
    } catch (err) {
      $("#eventError").textContent = err.message;
      $("#eventError").hidden = false;
    }
  };
  dlg.showModal();
}

function syncTypeFields() {
  const f = $("#eventForm");
  const type = f.querySelector("input[name=type]:checked").value;
  f.querySelectorAll("[data-show]").forEach((el) => { el.hidden = !el.dataset.show.split(" ").includes(type); });
  const placeholders = { bill: "e.g. Electric bill", paycheck: "e.g. Bonus check", birthday: "e.g. Emma's birthday", dayoff: "e.g. Doctor appointment", other: "e.g. Soccer practice" };
  f.title.placeholder = placeholders[type];
  // Birthdays almost always repeat yearly; suggest it when adding one.
  if (type === "birthday" && f.recurrence.value === "none" && !f.title.value) f.recurrence.value = "yearly";
}
$("#typePills").addEventListener("change", syncTypeFields);

// ---------- Paycheck breakdown ----------
function openPaycheck(o) {
  const p = o.paycheck;
  const fmt = (s) => parseIso(s).toLocaleDateString(undefined, { month: "short", day: "numeric" });
  $("#payTitle").textContent = `${o.title} · ${fmt(o.date)}`;
  $("#payBody").innerHTML = `
    <table class="breakdown">
      <tr><td>Pay period</td><td>${fmt(p.periodStart)} – ${fmt(p.periodEnd)}</td></tr>
      <tr><td>Hours paid</td><td>${p.hoursPaid}</td></tr>
      ${p.unpaidHoursOff ? `<tr><td>Unpaid time off</td><td class="neg">−${p.unpaidHoursOff} h</td></tr>` : ""}
      <tr><td>Gross pay</td><td>${money(p.gross)}</td></tr>
      <tr><td>Taxes (estimated)</td><td class="neg">−${money(p.taxes)}</td></tr>
      <tr><td>Other deductions</td><td class="neg">−${money(p.deductions)}</td></tr>
      <tr class="net"><td>Take-home</td><td class="pos">${money(p.net)}</td></tr>
    </table>`;
  $("#payEditJob").onclick = () => {
    $("#payDialog").close();
    openJobForm(state.jobs.find((j) => j.id === o.sourceId));
  };
  $("#payDialog").showModal();
}

// ---------- Jobs ----------
function openJobs() {
  const list = $("#jobList");
  list.innerHTML = state.jobs.length ? "" : `<li class="empty">No jobs yet.</li>`;
  for (const j of state.jobs) {
    const li = document.createElement("li");
    const rate = j.payType === "salary" ? `${money(j.rate, { maximumFractionDigits: 0 })}/yr` : `${money(j.rate)}/hr · ${j.hoursPerWeek} h/wk`;
    li.innerHTML = `<div class="info"><div><b>${esc(j.name)}</b>${j.person ? " · " + esc(j.person) : ""}</div>` +
      `<div class="sub">${rate} · paid ${FREQ_LABEL[j.frequency]} · ${j.taxPercent}% tax</div></div><span class="muted">Edit ›</span>`;
    li.onclick = () => { $("#jobsDialog").close(); openJobForm(j); };
    list.appendChild(li);
  }
  $("#jobsDialog").showModal();
}

function openJobForm(job) {
  const dlg = $("#jobDialog");
  const f = $("#jobForm");
  f.reset();
  $("#jobError").hidden = true;
  $("#jobDialogTitle").textContent = job ? "Edit job" : "Add job";
  const j = job || { payType: "hourly", hoursPerWeek: 40, hoursPerDay: 8, frequency: "biweekly", anchorPayDate: iso(new Date()), taxPercent: 20, deductionsPerCheck: 0 };
  for (const k of ["name", "person", "payType", "rate", "hoursPerWeek", "hoursPerDay", "frequency", "anchorPayDate", "taxPercent", "deductionsPerCheck"])
    f[k].value = j[k] ?? "";
  const syncRate = () => { $("#rateLabel").textContent = f.payType.value === "salary" ? "Yearly salary ($)" : "Hourly rate ($)"; };
  f.payType.onchange = syncRate;
  syncRate();

  const del = $("#deleteJob");
  del.hidden = !job;
  del.onclick = async () => {
    if (!confirm(`Delete job "${job.name}"? Its paydays will disappear from the calendar.`)) return;
    await api(`/api/jobs/${job.id}`, { method: "DELETE" });
    dlg.close();
    refresh();
  };

  f.onsubmit = async (e) => {
    e.preventDefault();
    const num = (v) => (v === "" ? 0 : Number(v));
    const body = {
      name: f.name.value.trim(), person: f.person.value.trim(), payType: f.payType.value,
      rate: num(f.rate.value), hoursPerWeek: num(f.hoursPerWeek.value), hoursPerDay: num(f.hoursPerDay.value) || 8,
      frequency: f.frequency.value, anchorPayDate: f.anchorPayDate.value,
      taxPercent: num(f.taxPercent.value), deductionsPerCheck: num(f.deductionsPerCheck.value),
    };
    try {
      if (job) await api(`/api/jobs/${job.id}`, { method: "PUT", body });
      else await api("/api/jobs", { method: "POST", body });
      dlg.close();
      await refresh();
    } catch (err) {
      $("#jobError").textContent = err.message;
      $("#jobError").hidden = false;
    }
  };
  dlg.showModal();
}

// ---------- Balance & settings ----------
function openBalance() {
  const f = $("#balanceForm");
  f.balance.value = state.settings.balance;
  f.asOf.value = iso(new Date());
  f.onsubmit = async (e) => {
    e.preventDefault();
    await api("/api/settings", { method: "PUT", body: { balance: Number(f.balance.value), balanceAsOf: f.asOf.value } });
    $("#balanceDialog").close();
    refresh();
  };
  $("#balanceDialog").showModal();
}

function openSettings() {
  const f = $("#settingsForm");
  f.familyName.value = state.settings.familyName;
  f.onsubmit = async (e) => {
    e.preventDefault();
    await api("/api/settings", { method: "PUT", body: { familyName: f.familyName.value.trim() } });
    $("#settingsDialog").close();
    refresh();
  };
  $("#settingsDialog").showModal();
}

async function openPhone() {
  const info = await api("/api/connect");
  const url = info.urls[0];
  $("#connectUrls").innerHTML = info.urls.length
    ? info.urls.map((u) => `<div>${esc(u)}</div>`).join("")
    : `<div class="error">This PC doesn't seem to be on a network.</div>`;
  $("#qr").innerHTML = url ? `<img alt="QR code for ${esc(url)}" src="/api/connect/qr.svg?url=${encodeURIComponent(url)}">` : "";
  $("#pinValue").textContent = info.pin;
  $("#newPin").onclick = async () => {
    if (!confirm("Make a new PIN? Phones already connected will need the new one.")) return;
    const r = await api("/api/connect/new-pin", { method: "POST" });
    $("#pinValue").textContent = r.pin;
  };
  $("#phoneDialog").showModal();
}

// ---------- Wiring ----------
document.querySelectorAll("[data-close]").forEach((b) => b.addEventListener("click", () => b.closest("dialog").close()));
document.querySelectorAll("dialog").forEach((d) =>
  d.addEventListener("click", (e) => { if (e.target === d && d.id !== "pinDialog") d.close(); })); // click outside closes

const shiftMonth = (delta) => {
  const d = new Date(state.year, state.month + delta, 1);
  state.year = d.getFullYear(); state.month = d.getMonth();
  refresh();
};
$("#prevMonth").onclick = () => shiftMonth(-1);
$("#nextMonth").onclick = () => shiftMonth(1);
$("#todayBtn").onclick = () => { const t = new Date(); state.year = t.getFullYear(); state.month = t.getMonth(); refresh(); };
$("#addBtn").onclick = () => openEventForm(null);
$("#jobsBtn").onclick = openJobs;
$("#jobsBtnMobile").onclick = openJobs;
$("#addJob").onclick = () => { $("#jobsDialog").close(); openJobForm(null); };
$("#editBalance").onclick = openBalance;
$("#settingsBtn").onclick = openSettings;
$("#settingsBtnMobile").onclick = openSettings;
$("#phoneBtn").onclick = openPhone;

// Swipe left/right on the calendar to change months (phones).
let touchX = null;
$("#grid").addEventListener("touchstart", (e) => { touchX = e.touches[0].clientX; }, { passive: true });
$("#grid").addEventListener("touchend", (e) => {
  if (touchX == null) return;
  const dx = e.changedTouches[0].clientX - touchX;
  if (Math.abs(dx) > 60) shiftMonth(dx < 0 ? 1 : -1);
  touchX = null;
});

// Keep phones and the PC in sync when someone else adds something.
document.addEventListener("visibilitychange", () => { if (!document.hidden) refresh(); });
setInterval(() => { if (!document.hidden && !document.querySelector("dialog[open]")) refresh(); }, 30000);

(async function start() {
  const who = await api("/api/whoami");
  state.local = who.local;
  document.querySelectorAll(".local-only").forEach((el) => { el.hidden = !who.local; });
  await refresh();
})().catch((err) => alert(err.message));
