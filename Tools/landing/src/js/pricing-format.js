// Pure pricing helpers. Numbers mirror Assets/Scripts/Billing/PlanCatalog.cs — when the
// catalog changes, change PLANS here (pricing.test.mjs pins the values).
export const PLANS = [
  { id: 'start', name: 'Старт', forWhom: 'Для одной точки', month: 9990, year: 99000, bots: 1, channels: 1, dialogs: 300 },
  { id: 'business', name: 'Бизнес', forWhom: 'Для растущего бизнеса', month: 19990, year: 198990, bots: 3, channels: 3, dialogs: 1000, popular: true },
  { id: 'network', name: 'Сеть', forWhom: 'Для сети точек', month: 39900, year: 399990, bots: 5, channels: 5, dialogs: 3000 },
];

export function formatKzt(n) {
  return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
}

export function yearlyMonthly(yearKzt) {
  return Math.floor(yearKzt / 12);
}

export function savingsPercent(monthKzt, yearKzt) {
  return Math.floor((1 - yearKzt / (monthKzt * 12)) * 100);
}
