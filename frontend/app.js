"use strict";

const rowsElement = document.getElementById("currency-rows");
const currencyPicker = document.getElementById("currency-picker");
const currencySearch = document.getElementById("currency-search");
const currencyOptions = document.getElementById("currency-options");
const saveButton = document.getElementById("save-button");
const retryButton = document.getElementById("retry-button");
const rateStatus = document.getElementById("rate-status");
const saveStatus = document.getElementById("save-status");
const converterError = document.getElementById("converter-error");
const historyButton = document.getElementById("history-button");
const historyContent = document.getElementById("history-content");

const preferredCodes = ["RUB", "GEL", "USD", "EUR"];
const flagRegions = {
  ANG: "CW",
  CNH: "CN",
  EUR: "EU",
  XAF: null,
  XCD: null,
  XCG: null,
  XOF: null,
  XPF: null,
  ZWG: "ZW"
};
const localNames = {
  RUB: "Российский рубль",
  GEL: "Грузинский лари",
  USD: "Доллар США",
  EUR: "Евро",
  GBP: "Фунт стерлингов",
  GGP: "Фунт Гернси",
  IMP: "Фунт острова Мэн",
  JEP: "Фунт Джерси",
  CNH: "Китайский офшорный юань",
  CNY: "Китайский юань",
  JPY: "Японская иена",
  SLE: "Леоне Сьерра-Леоне",
  TRY: "Турецкая лира",
  XCG: "Карибский гульден",
  ZWG: "Зимбабвийский золотой"
};
const maxRows = 10;
const russianCurrencyNames = typeof Intl.DisplayNames === "function"
  ? new Intl.DisplayNames("ru-RU", { type: "currency" }) : null;
const englishCurrencyNames = typeof Intl.DisplayNames === "function"
  ? new Intl.DisplayNames("en-US", { type: "currency" }) : null;
const amountFormatters = [6, 10, 14].map(maximumFractionDigits =>
  new Intl.NumberFormat("ru-RU", { maximumFractionDigits }));
const dateFormatter = new Intl.DateTimeFormat("ru-RU", { dateStyle: "medium" });
const historyDateFormatter = new Intl.DateTimeFormat("ru-RU", { dateStyle: "short", timeStyle: "short" });

let market = null;
let byCode = new Map();
let namesByCode = new Map();
let visibleCodes = [];
let activeCode = null;
let activeRaw = "1000";
let historyLoaded = false;
let saving = false;
let pickerOpen = false;
let matchedCurrencies = [];
let highlightedIndex = -1;

function make(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;
  return node;
}

function normalizeSearch(value) {
  return value.toLocaleLowerCase("ru-RU").normalize("NFD").replace(/\p{M}/gu, "");
}

function currencyNames(item) {
  const russian = russianCurrencyNames?.of(item.code);
  const english = englishCurrencyNames?.of(item.code);
  const display = localNames[item.code] || (russian && russian !== item.code
    ? russian[0].toLocaleUpperCase("ru-RU") + russian.slice(1) : item.name);
  return {
    display,
    search: [item.code, display, russian, english, item.name]
      .filter(Boolean).map(normalizeSearch)
  };
}

function currencyRegion(code) {
  return Object.hasOwn(flagRegions, code) ? flagRegions[code] : code.slice(0, 2);
}

function currencyFlag(code) {
  const region = currencyRegion(code);
  if (!region) {
    const shared = make("span", "currency-flag currency-flag-shared", "🌐");
    shared.setAttribute("aria-hidden", "true");
    return shared;
  }
  const flag = make("img", "currency-flag");
  flag.src = `/flags/${region.toLowerCase()}.svg`;
  flag.alt = "";
  flag.width = 28;
  flag.height = 21;
  flag.onerror = () => {
    const shared = make("span", "currency-flag currency-flag-shared", "🌐");
    shared.setAttribute("aria-hidden", "true");
    flag.replaceWith(shared);
  };
  return flag;
}

function showError(message) {
  converterError.textContent = message;
  converterError.hidden = !message;
}

function parseAmount(value) {
  const normalized = value.trim().replace(/[\s\u00a0\u202f]/g, "").replace(",", ".");
  if (!/^(?:\d+\.?\d*|\.\d+)$/.test(normalized)) return null;
  const number = Number(normalized);
  return Number.isFinite(number) && number >= 0 && number <= 1_000_000_000_000 ? number : null;
}

function formatAmount(number) {
  if (!Number.isFinite(number)) return "—";
  const absolute = Math.abs(number);
  const formatter = absolute >= 1 ? amountFormatters[0]
    : absolute >= .0001 ? amountFormatters[1] : amountFormatters[2];
  return formatter.format(number);
}

function formatDate(value, formatter = dateFormatter) {
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? "дата неизвестна" : formatter.format(date);
}

async function api(path, options = {}) {
  let response;
  try {
    response = await fetch(path, {
      ...options,
      headers: { Accept: "application/json", ...options.headers }
    });
  } catch {
    throw new Error("Нет связи с сервером. Проверьте запуск контейнеров.");
  }
  if (!response.ok) {
    if (response.status === 503) throw new Error("Курсы Frankfurter сейчас недоступны. Попробуйте ещё раз позже.");
    if (response.status === 400) throw new Error("Проверьте сумму и выбранные валюты.");
    if (response.status === 404) throw new Error("Запись уже удалена. Обновите историю.");
    throw new Error("Запрос не выполнен. Попробуйте ещё раз.");
  }
  return response.status === 204 ? null : response.json();
}

function currentAmount() {
  return parseAmount(activeRaw);
}

function updateSaveButton() {
  const amount = currentAmount();
  saveButton.disabled = saving || !market || visibleCodes.length < 2 || amount === null || amount <= 0;
}

function updateValues() {
  const amount = currentAmount();
  const source = byCode.get(activeCode);
  for (const input of rowsElement.querySelectorAll(".currency-input")) {
    if (input.dataset.code === activeCode) continue;
    const target = byCode.get(input.dataset.code);
    input.value = amount === null || !source || !target
      ? ""
      : formatAmount(amount / source.rate * target.rate);
  }
  updateSaveButton();
}

function updateActiveRow() {
  for (const row of rowsElement.querySelectorAll(".currency-row")) {
    row.classList.toggle("is-active", row.dataset.code === activeCode);
  }
}

function closeCurrencyOptions() {
  pickerOpen = false;
  matchedCurrencies = [];
  highlightedIndex = -1;
  currencyOptions.hidden = true;
  currencySearch.setAttribute("aria-expanded", "false");
  currencySearch.removeAttribute("aria-activedescendant");
}

function highlightCurrency(index) {
  highlightedIndex = index;
  for (const option of currencyOptions.querySelectorAll(".currency-option")) {
    const selected = Number(option.dataset.index) === index;
    option.classList.toggle("is-highlighted", selected);
    option.setAttribute("aria-selected", String(selected));
    if (selected) option.scrollIntoView({ block: "nearest" });
  }
  if (index >= 0) currencySearch.setAttribute("aria-activedescendant", `currency-option-${matchedCurrencies[index].code}`);
  else currencySearch.removeAttribute("aria-activedescendant");
}

function renderCurrencyOptions() {
  if (!pickerOpen || !market || visibleCodes.length >= maxRows) return;
  const query = normalizeSearch(currencySearch.value.trim());
  matchedCurrencies = market.currencies
    .filter(item => !visibleCodes.includes(item.code))
    .filter(item => !query || namesByCode.get(item.code).search.some(name => name.includes(query)));
  if (query) {
    const rank = item => {
      if (item.code.toLowerCase() === query) return 0;
      if (item.code.toLowerCase().startsWith(query)) return 1;
      if (normalizeSearch(namesByCode.get(item.code).display).startsWith(query)) return 2;
      return 3;
    };
    matchedCurrencies.sort((left, right) => rank(left) - rank(right) || left.code.localeCompare(right.code));
  }
  currencyOptions.replaceChildren();
  if (!matchedCurrencies.length) {
    currencyOptions.append(make("p", "currency-option-empty", "Валюта не найдена"));
  }
  matchedCurrencies.forEach((item, index) => {
    const option = make("button", "currency-option");
    option.type = "button";
    option.id = `currency-option-${item.code}`;
    option.dataset.index = index;
    option.tabIndex = -1;
    option.setAttribute("role", "option");
    option.append(currencyFlag(item.code));
    option.append(make("span", "currency-option-code", item.code));
    option.append(make("span", "currency-option-name", namesByCode.get(item.code).display));
    option.addEventListener("pointerdown", event => event.preventDefault());
    option.addEventListener("mouseenter", () => highlightCurrency(index));
    option.addEventListener("click", () => addCurrency(item.code));
    currencyOptions.append(option);
  });
  currencyOptions.hidden = false;
  currencySearch.setAttribute("aria-expanded", "true");
  highlightCurrency(query && matchedCurrencies.length ? 0 : -1);
}

function refreshCurrencySearch() {
  const available = market && visibleCodes.length < maxRows;
  currencySearch.disabled = !available;
  currencySearch.placeholder = available
    ? "+ Найти валюту по коду или названию"
    : market ? "Можно добавить не больше 10 валют" : "Загрузка валют…";
  if (!available) closeCurrencyOptions();
  else renderCurrencyOptions();
}

function addCurrency(code) {
  if (!byCode.has(code) || visibleCodes.includes(code) || visibleCodes.length >= maxRows) return;
  visibleCodes.push(code);
  currencySearch.value = "";
  closeCurrencyOptions();
  renderRows();
}

function renderRows() {
  rowsElement.replaceChildren();
  for (const code of visibleCodes) {
    const row = make("div", "currency-row");
    row.dataset.code = code;
    const label = make("div", "currency-label");
    const flag = currencyFlag(code);
    flag.setAttribute("aria-hidden", "true");
    label.append(flag);
    label.append(make("span", "currency-code", code));
    label.append(make("span", "currency-name", namesByCode.get(code).display));

    const input = make("input", "currency-input");
    input.type = "text";
    input.inputMode = "decimal";
    input.autocomplete = "off";
    input.placeholder = "0";
    input.dataset.code = code;
    input.setAttribute("aria-label", `Сумма в ${namesByCode.get(code).display} (${code})`);
    if (code === activeCode) input.value = activeRaw;
    input.addEventListener("input", () => {
      activeCode = code;
      activeRaw = input.value;
      saveStatus.textContent = "";
      showError(activeRaw.trim() && currentAmount() === null
        ? "Введите число от 0 до 1 000 000 000 000."
        : "");
      updateActiveRow();
      updateValues();
    });

    const remove = make("button", "remove-button", "×");
    remove.type = "button";
    remove.title = `Убрать ${code}`;
    remove.setAttribute("aria-label", `Убрать ${code}`);
    remove.disabled = visibleCodes.length <= 2;
    remove.addEventListener("click", () => {
      if (visibleCodes.length <= 2) return;
      if (activeCode === code) {
        const replacement = visibleCodes.find(item => item !== code);
        activeRaw = rowsElement.querySelector(`.currency-input[data-code="${replacement}"]`).value;
        activeCode = replacement;
      }
      visibleCodes = visibleCodes.filter(item => item !== code);
      renderRows();
    });
    row.append(label, input, remove);
    rowsElement.append(row);
  }
  updateActiveRow();
  updateValues();
  refreshCurrencySearch();
  const shown = visibleCodes.map(code => byCode.get(code).asOfUtc);
  rateStatus.textContent = `${market.source} · ${market.currencies.length} валют · курсы от ${formatDate(Math.min(...shown.map(value => new Date(value).getTime())))}`;
}

async function loadMarket() {
  retryButton.hidden = true;
  showError("");
  rateStatus.textContent = "Загружаем курсы…";
  try {
    const data = await api("/api/market");
    if (!data || !Array.isArray(data.currencies) || data.currencies.length < 30) {
      throw new Error("Сервис вернул неполный список валют.");
    }
    market = data;
    byCode = new Map(data.currencies.map(item => [item.code, item]));
    namesByCode = new Map(data.currencies.map(item => [item.code, currencyNames(item)]));
    visibleCodes = preferredCodes.filter(code => byCode.has(code));
    if (visibleCodes.length < 2) visibleCodes = data.currencies.slice(0, 4).map(item => item.code);
    activeCode = visibleCodes[0];
    activeRaw = "1000";
    renderRows();
  } catch (error) {
    rateStatus.textContent = "Курсы не загружены";
    showError(error.message);
    retryButton.hidden = false;
  }
}

currencySearch.addEventListener("focus", () => {
  pickerOpen = true;
  renderCurrencyOptions();
});
currencySearch.addEventListener("click", () => {
  if (!pickerOpen) {
    pickerOpen = true;
    renderCurrencyOptions();
  }
});
currencySearch.addEventListener("input", () => {
  pickerOpen = true;
  renderCurrencyOptions();
});
currencySearch.addEventListener("keydown", event => {
  if (event.key === "Escape") {
    event.preventDefault();
    closeCurrencyOptions();
    return;
  }
  if (event.key === "Tab") {
    closeCurrencyOptions();
    return;
  }
  if (event.key === "ArrowDown" || event.key === "ArrowUp") {
    event.preventDefault();
    if (!pickerOpen) {
      pickerOpen = true;
      renderCurrencyOptions();
    }
    if (matchedCurrencies.length) {
      const step = event.key === "ArrowDown" ? 1 : -1;
      const next = highlightedIndex < 0
        ? step > 0 ? 0 : matchedCurrencies.length - 1
        : (highlightedIndex + step + matchedCurrencies.length) % matchedCurrencies.length;
      highlightCurrency(next);
    }
  }
  if (event.key === "Enter" && pickerOpen && highlightedIndex >= 0) {
    event.preventDefault();
    addCurrency(matchedCurrencies[highlightedIndex].code);
  }
});
document.addEventListener("pointerdown", event => {
  if (!currencyPicker.contains(event.target)) closeCurrencyOptions();
});

retryButton.addEventListener("click", loadMarket);

saveButton.addEventListener("click", async () => {
  const amount = currentAmount();
  if (!market || amount === null || amount <= 0 || visibleCodes.length < 2) {
    showError("Введите сумму больше нуля в любой валюте.");
    return;
  }
  saveButton.disabled = true;
  saving = true;
  saveStatus.textContent = "Сохраняем…";
  showError("");
  try {
    const saved = await api("/api/conversions/batch", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        amount,
        fromCurrency: activeCode,
        toCurrencies: visibleCodes.filter(code => code !== activeCode)
      })
    });
    saveStatus.textContent = `Сохранено ${saved.length} расчётов`;
    if (historyLoaded) loadHistory().catch(() => {});
  } catch (error) {
    showError(error.message);
    saveStatus.textContent = "";
  } finally {
    saving = false;
    updateSaveButton();
  }
});

function showHistoryMessage(message) {
  historyContent.replaceChildren(make("p", "history-placeholder", message));
}

async function loadHistory() {
  historyButton.disabled = true;
  showHistoryMessage("Загружаем историю…");
  try {
    const records = await api("/api/conversions?limit=20");
    if (!Array.isArray(records)) throw new Error("Некорректный ответ сервера.");
    historyLoaded = true;
    historyButton.textContent = "Обновить историю ↗";
    if (!records.length) {
      showHistoryMessage("Расчётов пока нет. Сохраните первый результат выше.");
      return;
    }
    const list = make("div", "history-list");
    for (const record of records) {
      const row = make("article", "history-item");
      const source = make("div");
      source.append(make("strong", "history-pair", `${formatAmount(record.amount)} ${record.fromCurrency} → ${record.toCurrency}`));
      source.append(make("span", "history-meta", formatDate(record.createdAtUtc, historyDateFormatter)));
      const result = make("div", "history-result-wrap");
      result.append(make("strong", "history-result", `${formatAmount(record.result)} ${record.toCurrency}`));
      result.append(make("span", "history-meta", `Курс ${formatAmount(record.rate)} · ${record.rateSource}`));
      const remove = make("button", "history-delete", "×");
      remove.type = "button";
      remove.title = "Удалить запись";
      remove.setAttribute("aria-label", `Удалить запись ${record.id}`);
      remove.addEventListener("click", async () => {
        if (!window.confirm("Удалить этот расчёт из истории?")) return;
        remove.disabled = true;
        try {
          await api(`/api/conversions/${record.id}`, { method: "DELETE" });
          row.remove();
          if (!list.childElementCount) showHistoryMessage("Расчётов пока нет.");
        } catch (error) {
          row.append(make("p", "history-error", error.message));
          remove.disabled = false;
        }
      });
      row.append(source, result, remove);
      list.append(row);
    }
    historyContent.replaceChildren(list);
  } catch (error) {
    showHistoryMessage(`История не загрузилась: ${error.message}`);
    throw error;
  } finally {
    historyButton.disabled = false;
  }
}

historyButton.addEventListener("click", () => { loadHistory().catch(() => {}); });
loadMarket();
