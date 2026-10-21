import { Component, input, output } from '@angular/core';
import { ImportBatch } from '../api';
import { UtcDatePipe } from '../shared/utc-date.pipe';

@Component({
  selector: 'app-recent-imports',
  imports: [UtcDatePipe],
  templateUrl: './recent-imports.html',
  styleUrl: './recent-imports.css',
})
export class RecentImports {
  readonly batches = input.required<ImportBatch[]>();
  readonly rejectionsRequested = output<number>();

  showRejections(event: Event, batchId: number): void {
    event.preventDefault();
    this.rejectionsRequested.emit(batchId);
  }
}
