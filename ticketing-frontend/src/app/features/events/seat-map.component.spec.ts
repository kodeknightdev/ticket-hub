import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PageSection } from '../../core/models';
import { SeatMapComponent } from './seat-map.component';

function section(states: string[]): PageSection {
  return {
    id: 's1',
    name: 'Floor',
    rows: [{ row: 'A', seats: states.map((state, i) => ({ id: `seat-${i}`, number: i + 1, tierId: 't1', state: state as never })) }],
  };
}

describe('SeatMapComponent', () => {
  let fixture: ComponentFixture<SeatMapComponent>;
  let emitted: string[][];

  beforeEach(() => {
    fixture = TestBed.createComponent(SeatMapComponent);
    emitted = [];
    fixture.componentInstance.selectedChange.subscribe((v) => emitted.push(v));
    fixture.componentRef.setInput('tiers', [{ id: 't1', name: 'Standard', price: 50, basePrice: 50 }]);
  });

  it('disables seats that are sold or held', () => {
    fixture.componentRef.setInput('sections', [section(['available', 'sold', 'held'])]);
    fixture.detectChanges();
    const buttons = fixture.nativeElement.querySelectorAll('button.seat') as NodeListOf<HTMLButtonElement>;
    expect(buttons[0].disabled).toBeFalse();
    expect(buttons[1].disabled).toBeTrue();
    expect(buttons[2].disabled).toBeTrue();
  });

  it('toggles a seat in and out of the selection', () => {
    fixture.componentRef.setInput('sections', [section(['available', 'available'])]);
    fixture.componentRef.setInput('selected', ['seat-0']);
    fixture.detectChanges();
    fixture.componentInstance.toggle('seat-1');
    fixture.componentInstance.toggle('seat-0');
    expect(emitted).toEqual([['seat-0', 'seat-1'], []]);
  });

  it('stops at eight seats', () => {
    const states = Array.from({ length: 10 }, () => 'available');
    fixture.componentRef.setInput('sections', [section(states)]);
    fixture.componentRef.setInput('selected', Array.from({ length: 8 }, (_, i) => `seat-${i}`));
    fixture.detectChanges();
    fixture.componentInstance.toggle('seat-9');
    expect(emitted.length).toBe(0);
  });
});
