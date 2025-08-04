import { TestBed } from '@angular/core/testing';
import { provideEchartsCore } from 'ngx-echarts';
import { KpiCardComponent } from './kpi-card.component';

describe('KpiCardComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideEchartsCore({ echarts: () => import('echarts') })] });
  });

  it('formats money and shows a positive change as good news', () => {
    const fixture = TestBed.createComponent(KpiCardComponent);
    fixture.componentRef.setInput('kpi', { key: 'grossRevenue', value: 12500, previous: 10000, change: 0.25 });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('Gross revenue');
    expect(el.textContent).toContain('£12,500');
    expect(el.querySelector('.change')!.classList).toContain('good');
  });

  it('treats a rising refund rate as bad news', () => {
    const fixture = TestBed.createComponent(KpiCardComponent);
    fixture.componentRef.setInput('kpi', { key: 'refundRate', value: 0.05, previous: 0.02, change: 1.5 });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.textContent).toContain('5%');
    expect(el.querySelector('.change')!.classList).toContain('bad');
  });
});
