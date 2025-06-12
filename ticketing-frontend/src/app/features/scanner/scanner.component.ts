import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, ElementRef, OnDestroy, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { ApiService } from '../../core/api.service';
import { CheckInResult } from '../../core/models';

const MESSAGES: Record<CheckInResult['outcome'], string> = {
  admitted: 'Admit',
  'already-checked-in': 'Already checked in',
  voided: 'Ticket voided (refunded or cancelled)',
  'wrong-event': 'Ticket is for a different event',
  invalid: 'Not a valid ticket',
};

@Component({
  selector: 'app-scanner',
  imports: [FormsModule, DatePipe, MatButtonModule, MatFormFieldModule, MatInputModule],
  template: `
    <h1>Door scanner</h1>
    <div class="controls">
      <mat-form-field appearance="outline" class="code">
        <mat-label>Ticket code</mat-label>
        <input matInput [(ngModel)]="code" (keyup.enter)="submit(code)" name="code" autocomplete="off" />
      </mat-form-field>
      <button mat-flat-button color="primary" (click)="submit(code)" [disabled]="!code">Check in</button>
      <button mat-stroked-button (click)="scanning() ? stopCamera() : startCamera()">{{ scanning() ? 'Stop camera' : 'Use camera' }}</button>
    </div>
    <video #video [hidden]="!scanning()" muted playsinline></video>
    @if (result(); as r) {
      <div class="result" [class]="'result ' + r.outcome">
        <strong>{{ message(r) }}</strong>
        @if (r.ticket) { <span>{{ r.ticket.label }}</span> }
        @if (r.checkedInAt) { <span>at {{ r.checkedInAt | date: 'HH:mm:ss' }}</span> }
      </div>
    }
  `,
  styles: `
    .controls { display: flex; gap: 12px; align-items: center; flex-wrap: wrap; }
    .code { flex: 1; min-width: 280px; }
    video { width: 100%; max-width: 480px; border-radius: 8px; }
    .result { margin-top: 16px; padding: 16px; border-radius: 8px; display: flex; gap: 12px; font-size: 18px; color: #fff; }
    .admitted { background: #2e7d32; }
    .already-checked-in, .voided { background: #ef6c00; }
    .invalid, .wrong-event { background: #c62828; }
  `,
})
export class ScannerComponent implements OnDestroy {
  private readonly api = inject(ApiService);
  private readonly video = viewChild<ElementRef<HTMLVideoElement>>('video');
  private controls: { stop(): void } | null = null;
  private lastCode = '';

  code = '';
  readonly scanning = signal(false);
  readonly result = signal<CheckInResult | null>(null);

  message(result: CheckInResult): string {
    return MESSAGES[result.outcome];
  }

  submit(code: string): void {
    const trimmed = code.trim();
    if (!trimmed) return;
    this.api.checkIn(trimmed).subscribe({
      next: (r) => this.result.set(r),
      error: (e: HttpErrorResponse) => this.result.set(e.error?.outcome ? (e.error as CheckInResult) : { outcome: 'invalid' }),
    });
    this.code = '';
  }

  async startCamera(): Promise<void> {
    const { BrowserQRCodeReader } = await import('@zxing/browser');
    const reader = new BrowserQRCodeReader();
    this.scanning.set(true);
    this.controls = await reader.decodeFromVideoDevice(undefined, this.video()!.nativeElement, (res) => {
      const text = res?.getText();
      // the camera keeps seeing the same code for a while; only submit it once
      if (text && text !== this.lastCode) {
        this.lastCode = text;
        this.submit(text);
      }
    });
  }

  stopCamera(): void {
    this.controls?.stop();
    this.controls = null;
    this.scanning.set(false);
  }

  ngOnDestroy(): void {
    this.stopCamera();
  }
}
