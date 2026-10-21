import { DecimalPipe } from '@angular/common';
import { Component, HostListener, input, output } from '@angular/core';
import { TransactionDetail as Detail } from '../api';
import { UtcDatePipe } from '../shared/utc-date.pipe';

// popup with one transaction and its audit history
@Component({
  selector: 'app-transaction-detail',
  imports: [DecimalPipe, UtcDatePipe],
  templateUrl: './transaction-detail.html',
  styleUrl: './transaction-detail.css',
})
export class TransactionDetail {
  readonly detail = input.required<Detail>();
  readonly closed = output<void>();

  @HostListener('document:keydown.escape')
  close(): void {
    this.closed.emit();
  }
}
