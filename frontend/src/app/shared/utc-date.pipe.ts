import { formatDate } from '@angular/common';
import { Pipe, PipeTransform } from '@angular/core';

// the API sends UTC timestamps; show them as UTC so the grid matches the database
@Pipe({ name: 'utcDate' })
export class UtcDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, format = 'yyyy-MM-dd HH:mm:ss'): string {
    return value ? formatDate(value, format, 'en-US', 'UTC') : '';
  }
}
