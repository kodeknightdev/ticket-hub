import { demandHeatmap, horizontalBars, salesCurve, sparkline } from './charts';

describe('chart builders', () => {
  it('draws the forecast band on top of the last actual point', () => {
    const option = salesCurve({
      event: { eventId: 'e', title: 't', artist: 'a', venue: 'v', city: 'c', capacity: 500, startsAt: '', publishedAt: '', status: 'published' },
      actual: [{ date: '2025-06-01', tickets: 10, cumulative: 10 }, { date: '2025-06-02', tickets: 5, cumulative: 15 }],
      velocity: [],
      forecast: [{ date: '2025-06-03', predicted: 4, lower: 2, upper: 6, cumulative: 19, cumulativeLower: 17, cumulativeUpper: 21 }],
      summary: null,
      priceChanges: [],
      sections: [],
    });
    const series = option.series as { name: string; data: (number | null)[] }[];
    const actual = series.find((s) => s.name === 'Actual')!;
    const forecast = series.find((s) => s.name === 'Forecast')!;
    const band = series.find((s) => s.name === 'band')!;
    expect(actual.data).toEqual([10, 15, null]);
    expect(forecast.data).toEqual([null, 15, 19]);
    expect(band.data).toEqual([null, 0, 4]);
    expect((option.xAxis as { data: string[] }).data.length).toBe(3);
  });

  it('flattens the weekday by hour matrix into heatmap points', () => {
    const matrix = Array.from({ length: 7 }, (_, d) => Array.from({ length: 24 }, (_, h) => d * 100 + h));
    const option = demandHeatmap({ weekdays: ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'], hours: [...Array(24).keys()], matrix });
    const points = (option.series as { data: number[][] }[])[0].data;
    expect(points.length).toBe(168);
    expect(points).toContain([5, 2, 205]);
  });

  it('lists the biggest bar first', () => {
    const option = horizontalBars(['A', 'B'], [300, 100], 'Revenue');
    expect((option.yAxis as { data: string[] }).data).toEqual(['B', 'A']);
  });

  it('keeps sparklines axis-free', () => {
    expect((sparkline([1, 2, 3]).xAxis as { show: boolean }).show).toBeFalse();
  });
});
