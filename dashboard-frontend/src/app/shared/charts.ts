import type { EChartsOption } from 'echarts';
import { Funnel, Heatmap, Insights, LeadTime, RevenueForecast, SectionHeatmap, TimePoint } from '../core/models';

const money = (v: number) => `£${Math.round(v).toLocaleString('en-GB')}`;

export function sparkline(values: number[], color = '#3949ab'): EChartsOption {
  return {
    grid: { left: 0, right: 0, top: 4, bottom: 0 },
    xAxis: { type: 'category', show: false, data: values.map((_, i) => i) },
    yAxis: { type: 'value', show: false, scale: true },
    series: [{ type: 'line', data: values, smooth: true, symbol: 'none', lineStyle: { width: 2, color }, areaStyle: { opacity: 0.1, color } }],
    tooltip: { show: false },
  };
}

export function salesOverTime(points: TimePoint[]): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['Revenue', 'Net revenue', 'Tickets'] },
    grid: { left: 60, right: 60, top: 40, bottom: 40 },
    xAxis: { type: 'category', data: points.map((p) => p.date) },
    yAxis: [
      { type: 'value', name: 'Revenue', axisLabel: { formatter: (v: number) => money(v) } },
      { type: 'value', name: 'Tickets' },
    ],
    series: [
      { name: 'Revenue', type: 'bar', data: points.map((p) => p.revenue), itemStyle: { color: '#5c6bc0' } },
      { name: 'Net revenue', type: 'line', data: points.map((p) => p.netRevenue), smooth: true, itemStyle: { color: '#26a69a' } },
      { name: 'Tickets', type: 'line', yAxisIndex: 1, data: points.map((p) => p.tickets), smooth: true, itemStyle: { color: '#ffa726' } },
    ],
  };
}

export function horizontalBars(labels: string[], values: number[], name: string, asMoney = true): EChartsOption {
  return {
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' } },
    grid: { left: 160, right: 30, top: 10, bottom: 30 },
    xAxis: { type: 'value', axisLabel: { formatter: (v: number) => (asMoney ? money(v) : `${v}`) } },
    yAxis: { type: 'category', data: [...labels].reverse(), axisLabel: { width: 150, overflow: 'truncate' } },
    series: [{ name, type: 'bar', data: [...values].reverse(), itemStyle: { color: '#5c6bc0' } }],
  };
}

export function sectionHeatmap(data: SectionHeatmap): EChartsOption {
  return {
    tooltip: { formatter: (p: unknown) => {
      const v = (p as { value: [number, number, number] }).value;
      return `${data.events[v[1]]}<br/>${data.sections[v[0]]}: ${Math.round(v[2] * 100)}%`;
    } },
    grid: { left: 180, right: 20, top: 10, bottom: 60 },
    xAxis: { type: 'category', data: data.sections, splitArea: { show: true }, axisLabel: { rotate: 30 } },
    yAxis: { type: 'category', data: data.events, splitArea: { show: true }, axisLabel: { width: 170, overflow: 'truncate' } },
    visualMap: { min: 0, max: 1, calculable: true, orient: 'horizontal', left: 'center', bottom: 0, show: false, inRange: { color: ['#fff3e0', '#ff7043', '#b71c1c'] } },
    series: [{
      type: 'heatmap',
      data: data.cells.map((c) => [data.sections.indexOf(c.section), data.events.indexOf(c.event), c.fill]),
      label: { show: true, formatter: (p: unknown) => `${Math.round(((p as { value: number[] }).value[2]) * 100)}%` },
    }],
  };
}

export function demandHeatmap(data: Heatmap): EChartsOption {
  const max = Math.max(1, ...data.matrix.flat());
  return {
    tooltip: { formatter: (p: unknown) => {
      const v = (p as { value: [number, number, number] }).value;
      return `${data.weekdays[v[1]]} ${String(v[0]).padStart(2, '0')}:00 · ${v[2]} tickets`;
    } },
    grid: { left: 50, right: 20, top: 10, bottom: 70 },
    xAxis: { type: 'category', data: data.hours.map((h) => `${h}:00`), splitArea: { show: true } },
    yAxis: { type: 'category', data: data.weekdays, splitArea: { show: true } },
    visualMap: { min: 0, max, calculable: true, orient: 'horizontal', left: 'center', bottom: 0, inRange: { color: ['#e8eaf6', '#3949ab', '#1a237e'] } },
    series: [{ type: 'heatmap', data: data.matrix.flatMap((row, d) => row.map((v, h) => [h, d, v])) }],
  };
}

export function leadTime(data: LeadTime): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    grid: { left: 50, right: 20, top: 20, bottom: 40 },
    xAxis: { type: 'category', data: data.buckets.map((b) => b.label) },
    yAxis: { type: 'value', name: 'Tickets' },
    series: [{ type: 'bar', data: data.buckets.map((b) => b.tickets), itemStyle: { color: '#26a69a' } }],
  };
}

export function funnel(data: Funnel): EChartsOption {
  return {
    tooltip: { trigger: 'item', formatter: '{b}: {c} seats' },
    series: [{
      type: 'funnel', left: '10%', width: '80%', sort: 'descending', gap: 4,
      label: { show: true, position: 'inside', formatter: '{b}\n{c}' },
      data: data.stages.map((s) => ({ name: s.stage, value: s.value })),
    }],
  };
}

/** Actual cumulative sales, then the forecast with its 80% band drawn as a stacked area. */
export function salesCurve(data: Insights['insights']): EChartsOption {
  const dates = [...data.actual.map((a) => a.date), ...data.forecast.map((f) => f.date)];
  const pad = (n: number) => Array<number | null>(n).fill(null);
  const lastActual = data.actual.at(-1)?.cumulative ?? null;
  const lower = [...pad(data.actual.length - 1), lastActual, ...data.forecast.map((f) => f.cumulativeLower)];
  const band = [...pad(data.actual.length - 1), 0, ...data.forecast.map((f) => f.cumulativeUpper - f.cumulativeLower)];
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['Actual', 'Forecast', 'Capacity'] },
    grid: { left: 60, right: 30, top: 40, bottom: 40 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: { type: 'value', name: 'Tickets sold' },
    series: [
      { name: 'Actual', type: 'line', data: [...data.actual.map((a) => a.cumulative), ...pad(data.forecast.length)], symbol: 'none', lineStyle: { width: 3 }, itemStyle: { color: '#3949ab' } },
      { name: 'band-lower', type: 'line', data: lower, stack: 'band', symbol: 'none', lineStyle: { opacity: 0 }, tooltip: { show: false } },
      { name: 'band', type: 'line', data: band, stack: 'band', symbol: 'none', lineStyle: { opacity: 0 }, areaStyle: { color: 'rgba(255,152,0,0.2)' }, tooltip: { show: false } },
      { name: 'Forecast', type: 'line', data: [...pad(data.actual.length - 1), lastActual, ...data.forecast.map((f) => f.cumulative)], symbol: 'none', lineStyle: { type: 'dashed', width: 2 }, itemStyle: { color: '#ef6c00' } },
      { name: 'Capacity', type: 'line', data: dates.map(() => data.event.capacity), symbol: 'none', lineStyle: { type: 'dotted', color: '#9e9e9e' }, itemStyle: { color: '#9e9e9e' } },
    ],
  };
}

export function velocityWithPrices(data: Insights['insights']): EChartsOption {
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['Tickets/day (7-day avg)', 'Price changes'] },
    grid: { left: 60, right: 60, top: 40, bottom: 40 },
    xAxis: { type: 'category', data: data.velocity.map((v) => v.date) },
    yAxis: [{ type: 'value', name: 'Tickets/day' }, { type: 'value', name: 'Price', axisLabel: { formatter: (v: number) => money(v) } }],
    series: [
      { name: 'Tickets/day (7-day avg)', type: 'line', data: data.velocity.map((v) => v.value), smooth: true, symbol: 'none', areaStyle: { opacity: 0.1 } },
      {
        name: 'Price changes', type: 'scatter', yAxisIndex: 1, symbolSize: 10,
        data: data.priceChanges.map((p) => [p.changedAt.slice(0, 10), p.newPrice]),
        itemStyle: { color: '#e53935' },
      },
    ],
  };
}

export function revenueForecast(data: RevenueForecast): EChartsOption {
  const dates = [...data.history.map((h) => h.date), ...data.forecast.map((f) => f.date)];
  const pad = (n: number) => Array<number | null>(n).fill(null);
  return {
    tooltip: { trigger: 'axis' },
    legend: { data: ['Actual', 'Forecast'] },
    grid: { left: 70, right: 30, top: 40, bottom: 40 },
    xAxis: { type: 'category', data: dates, boundaryGap: false },
    yAxis: { type: 'value', axisLabel: { formatter: (v: number) => money(v) } },
    series: [
      { name: 'Actual', type: 'line', data: [...data.history.map((h) => h.revenue), ...pad(data.forecast.length)], symbol: 'none', itemStyle: { color: '#3949ab' } },
      { name: 'lower', type: 'line', data: [...pad(data.history.length), ...data.forecast.map((f) => f.lower)], stack: 'band', symbol: 'none', lineStyle: { opacity: 0 }, tooltip: { show: false } },
      { name: 'band', type: 'line', data: [...pad(data.history.length), ...data.forecast.map((f) => f.upper - f.lower)], stack: 'band', symbol: 'none', lineStyle: { opacity: 0 }, areaStyle: { color: 'rgba(255,152,0,0.2)' }, tooltip: { show: false } },
      { name: 'Forecast', type: 'line', data: [...pad(data.history.length), ...data.forecast.map((f) => f.predicted)], symbol: 'none', lineStyle: { type: 'dashed' }, itemStyle: { color: '#ef6c00' } },
    ],
  };
}
