import { CurrencyPipe } from '@angular/common';
import { Component, computed, input, output } from '@angular/core';
import { PageSection, PageTier } from '../../core/models';

export const MAX_SEATS = 8;

@Component({
  selector: 'app-seat-map',
  imports: [CurrencyPipe],
  template: `
    <div class="legend">
      <span><i class="seat available"></i> available</span>
      <span><i class="seat held"></i> held</span>
      <span><i class="seat sold"></i> sold</span>
      <span><i class="seat selected"></i> your pick</span>
    </div>
    @for (section of sections(); track section.id) {
      <div class="section">
        <h3>{{ section.name }}</h3>
        @for (row of section.rows; track row.row) {
          <div class="row">
            <span class="label">{{ row.row }}</span>
            @for (seat of row.seats; track seat.id) {
              <button
                type="button"
                class="seat"
                [class]="'seat ' + (selected().includes(seat.id) ? 'selected' : seat.state)"
                [disabled]="seat.state !== 'available' && !selected().includes(seat.id)"
                [attr.aria-label]="section.name + ' ' + row.row + seat.number + ', ' + seat.state"
                [title]="row.row + seat.number + ' · ' + (tierPrice()[seat.tierId] | currency: 'GBP')"
                (click)="toggle(seat.id)"
              >
                {{ seat.number }}
              </button>
            }
          </div>
        }
      </div>
    }
  `,
  styles: `
    .legend { display: flex; gap: 16px; font-size: 13px; margin-bottom: 8px; }
    .legend .seat { display: inline-block; width: 14px; height: 14px; vertical-align: middle; }
    .section { margin-bottom: 16px; }
    .section h3 { margin: 8px 0; font-size: 15px; }
    .row { display: flex; align-items: center; gap: 4px; margin-bottom: 4px; flex-wrap: wrap; }
    .label { width: 24px; font-size: 12px; color: #666; }
    .seat { width: 28px; height: 26px; border: none; border-radius: 4px; font-size: 11px; cursor: pointer; padding: 0; }
    .seat.available { background: #c8e6c9; }
    .seat.held { background: #ffe082; cursor: not-allowed; }
    .seat.sold { background: #bdbdbd; cursor: not-allowed; color: #777; }
    .seat.unknown { background: #eceff1; }
    .seat.selected { background: #3949ab; color: #fff; }
  `,
})
export class SeatMapComponent {
  readonly sections = input.required<PageSection[]>();
  readonly tiers = input.required<PageTier[]>();
  readonly selected = input<string[]>([]);
  readonly selectedChange = output<string[]>();

  readonly tierPrice = computed(() => Object.fromEntries(this.tiers().map((t) => [t.id, t.price])));

  toggle(seatId: string): void {
    const current = this.selected();
    if (current.includes(seatId)) {
      this.selectedChange.emit(current.filter((id) => id !== seatId));
    } else if (current.length < MAX_SEATS) {
      this.selectedChange.emit([...current, seatId]);
    }
  }
}
