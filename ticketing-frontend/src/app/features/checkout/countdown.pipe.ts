import { Pipe, PipeTransform } from '@angular/core';

@Pipe({ name: 'countdown' })
export class CountdownPipe implements PipeTransform {
  transform(seconds: number | null | undefined): string {
    const s = Math.max(0, Math.floor(seconds ?? 0));
    return `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  }
}
