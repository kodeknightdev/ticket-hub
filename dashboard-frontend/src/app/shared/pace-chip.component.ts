import { Component, input } from '@angular/core';
import { PaceStatus } from '../core/models';

@Component({
  selector: 'app-pace-chip',
  template: `<span class="chip" [class]="'chip ' + cssClass()">{{ status() }}</span>`,
  styles: `
    .chip { padding: 2px 10px; border-radius: 12px; font-size: 12px; font-weight: 500; white-space: nowrap; }
    .on-track { background: #e8f5e9; color: #2e7d32; }
    .at-risk { background: #ffebee; color: #c62828; }
    .sold-out { background: #e8eaf6; color: #283593; }
    .completed, .cancelled { background: #eceff1; color: #546e7a; }
  `,
})
export class PaceChipComponent {
  readonly status = input.required<PaceStatus>();

  cssClass(): string {
    return this.status().toLowerCase().replace(/\s+/g, '-');
  }
}
