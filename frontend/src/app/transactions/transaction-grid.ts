import { DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, input, output, signal } from '@angular/core';
import { Api, Transaction, TransactionQuery } from '../api';
import { errorMessage } from '../shared/error-message';
import { UtcDatePipe } from '../shared/utc-date.pipe';

interface Column {
  key: string;
  label: string;
  numeric?: boolean;
}

@Component({
  selector: 'app-transaction-grid',
  imports: [DecimalPipe, UtcDatePipe],
  templateUrl: './transaction-grid.html',
  styleUrl: './transaction-grid.css',
})
export class TransactionGrid implements OnInit {
  private readonly api = inject(Api);

  // id of the row whose detail is open, so it can be highlighted
  readonly selectedId = input<number | null>(null);
  readonly rowSelected = output<Transaction>();
  readonly loaded = output<void>();
  readonly failed = output<string>();

  readonly sources = ['Store Uptown', 'Store Downtown', 'Online'];
  readonly statuses = ['Active', 'Revoked', 'Finalized'];
  readonly pageSizes = [10, 25, 50];
  readonly columns: Column[] = [
    { key: 'sourceSystem', label: 'Source System' },
    { key: 'transactionId', label: 'Transaction ID', numeric: true },
    { key: 'locationCode', label: 'Location Code' },
    { key: 'productName', label: 'Product Name' },
    { key: 'amount', label: 'Amount', numeric: true },
    { key: 'transactionTime', label: 'Transaction Time' },
    { key: 'status', label: 'Status' },
  ];

  readonly query = signal<TransactionQuery>({
    sourceSystem: '',
    status: '',
    search: '',
    sortBy: 'transactionTime',
    sortDir: 'desc',
    page: 1,
    pageSize: 10,
  });
  readonly rows = signal<Transaction[]>([]);
  readonly total = signal(0);
  readonly loading = signal(false);

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / this.query().pageSize)));

  ngOnInit(): void {
    this.reload();
  }

  reload(): void {
    this.loading.set(true);
    this.api.transactions(this.query()).subscribe({
      next: (page) => {
        this.rows.set(page.items);
        this.total.set(page.total);
        this.loading.set(false);
        this.loaded.emit();
      },
      error: (e) => {
        this.loading.set(false);
        this.failed.emit(errorMessage(e));
      },
    });
  }

  // changing a filter, sort or page size always goes back to the first page
  updateQuery(change: Partial<TransactionQuery>): void {
    this.query.update((q) => ({ ...q, ...change, page: 1 }));
    this.reload();
  }

  sort(column: Column): void {
    const q = this.query();
    const direction = q.sortBy === column.key && q.sortDir === 'asc' ? 'desc' : 'asc';
    this.updateQuery({ sortBy: column.key, sortDir: direction });
  }

  sortIndicator(column: Column): string {
    const q = this.query();
    if (q.sortBy !== column.key) return '';
    return q.sortDir === 'asc' ? '▲' : '▼';
  }

  ariaSort(column: Column): string {
    const q = this.query();
    if (q.sortBy !== column.key) return 'none';
    return q.sortDir === 'asc' ? 'ascending' : 'descending';
  }

  goToPage(page: number): void {
    const target = Math.min(Math.max(1, page), this.totalPages());
    this.query.update((q) => ({ ...q, page: target }));
    this.reload();
  }
}
