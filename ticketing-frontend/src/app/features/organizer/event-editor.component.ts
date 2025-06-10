import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormArray, FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
import { Router } from '@angular/router';
import { ApiService } from '../../core/api.service';
import { CreateEventRequest } from '../../core/models';

@Component({
  selector: 'app-event-editor',
  imports: [ReactiveFormsModule, MatFormFieldModule, MatInputModule, MatSelectModule, MatButtonModule],
  template: `
    <h1>New event</h1>
    <form [formGroup]="form" (ngSubmit)="save()">
      <div class="grid">
        <mat-form-field appearance="outline"><mat-label>Title</mat-label><input matInput formControlName="title" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Artist</mat-label><input matInput formControlName="artist" /></mat-form-field>
        <mat-form-field appearance="outline">
          <mat-label>Category</mat-label>
          <mat-select formControlName="category">
            @for (c of categories; track c) { <mat-option [value]="c">{{ c }}</mat-option> }
          </mat-select>
        </mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Date and time</mat-label><input matInput type="datetime-local" formControlName="startsAt" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>Venue</mat-label><input matInput formControlName="venueName" /></mat-form-field>
        <mat-form-field appearance="outline"><mat-label>City</mat-label><input matInput formControlName="city" /></mat-form-field>
        <mat-form-field appearance="outline" class="wide"><mat-label>Cover image URL</mat-label><input matInput formControlName="coverImageUrl" /></mat-form-field>
      </div>

      <h2>Price tiers</h2>
      <div formArrayName="tiers">
        @for (tier of tiers.controls; track $index; let i = $index) {
          <div class="line" [formGroupName]="i">
            <mat-form-field appearance="outline"><mat-label>Name</mat-label><input matInput formControlName="name" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Base £</mat-label><input matInput type="number" formControlName="basePrice" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Min £</mat-label><input matInput type="number" formControlName="minPrice" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Max £</mat-label><input matInput type="number" formControlName="maxPrice" /></mat-form-field>
          </div>
        }
      </div>
      <button mat-button type="button" (click)="addTier()">Add tier</button>

      <h2>Sections</h2>
      <div formArrayName="sections">
        @for (section of sections.controls; track $index; let i = $index) {
          <div class="line" [formGroupName]="i">
            <mat-form-field appearance="outline"><mat-label>Section</mat-label><input matInput formControlName="name" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Rows</mat-label><input matInput type="number" formControlName="rows" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Seats per row</mat-label><input matInput type="number" formControlName="seatsPerRow" /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>Tier</mat-label><input matInput formControlName="tier" /></mat-form-field>
          </div>
        }
      </div>
      <button mat-button type="button" (click)="addSection()">Add section</button>

      @if (error()) {
        <p class="error">{{ error() }}</p>
      }
      <div class="actions">
        <button mat-flat-button color="primary" type="submit" [disabled]="form.invalid || busy()">Save draft</button>
      </div>
    </form>
  `,
  styles: `
    .grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 0 12px; }
    .wide { grid-column: 1 / -1; }
    .line { display: flex; gap: 12px; flex-wrap: wrap; }
    .error { color: #c62828; white-space: pre-line; }
    .actions { margin-top: 16px; }
  `,
})
export class EventEditorComponent {
  private readonly fb = inject(FormBuilder);
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  readonly categories = ['Concert', 'Festival', 'Comedy', 'Theatre', 'Sport', 'Family'];
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = this.fb.nonNullable.group({
    title: ['', Validators.required],
    artist: ['', Validators.required],
    category: ['Concert', Validators.required],
    startsAt: ['', Validators.required],
    venueName: ['', Validators.required],
    city: ['', Validators.required],
    coverImageUrl: [''],
    tiers: this.fb.array([this.tierGroup('Standard', 50, 35, 90)]),
    sections: this.fb.array([this.sectionGroup('Floor', 10, 20, 'Standard')]),
  });

  get tiers(): FormArray {
    return this.form.controls.tiers;
  }

  get sections(): FormArray {
    return this.form.controls.sections;
  }

  addTier(): void {
    this.tiers.push(this.tierGroup('', 40, 30, 70));
  }

  addSection(): void {
    this.sections.push(this.sectionGroup('', 5, 10, this.tiers.at(0).value.name));
  }

  save(): void {
    const value = this.form.getRawValue();
    const request: CreateEventRequest = {
      ...value,
      startsAt: new Date(value.startsAt).toISOString(),
      coverImageUrl: value.coverImageUrl || null,
      description: null,
    };
    this.busy.set(true);
    this.api.createEvent(request).subscribe({
      next: () => void this.router.navigate(['/organizer']),
      error: (e: HttpErrorResponse) => {
        this.busy.set(false);
        const errors = e.error?.errors as Record<string, string[]> | undefined;
        this.error.set(errors ? Object.values(errors).flat().join('\n') : e.error?.title ?? 'Could not save the event');
      },
    });
  }

  private tierGroup(name: string, base: number, min: number, max: number) {
    return this.fb.nonNullable.group({
      name: [name, Validators.required],
      basePrice: [base, [Validators.required, Validators.min(1)]],
      minPrice: [min, [Validators.required, Validators.min(1)]],
      maxPrice: [max, [Validators.required, Validators.min(1)]],
    });
  }

  private sectionGroup(name: string, rows: number, seatsPerRow: number, tier: string) {
    return this.fb.nonNullable.group({
      name: [name, Validators.required],
      rows: [rows, [Validators.required, Validators.min(1), Validators.max(30)]],
      seatsPerRow: [seatsPerRow, [Validators.required, Validators.min(1), Validators.max(40)]],
      tier: [tier, Validators.required],
    });
  }
}
