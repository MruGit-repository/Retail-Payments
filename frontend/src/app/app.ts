import { Component, OnInit, inject, signal, viewChild } from '@angular/core';
import { Api, ImportBatch, ImportResult, Rejection, Transaction, TransactionDetail as Detail } from './api';
import { RecentImports } from './imports/recent-imports';
import { RejectionsPanel } from './imports/rejections-panel';
import { errorMessage } from './shared/error-message';
import { TransactionDetail } from './transactions/transaction-detail';
import { TransactionGrid } from './transactions/transaction-grid';

@Component({
  selector: 'app-root',
  imports: [TransactionGrid, TransactionDetail, RejectionsPanel, RecentImports],
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App implements OnInit {
  private readonly api = inject(Api);
  private readonly grid = viewChild.required(TransactionGrid);

  readonly error = signal<string | null>(null);

  readonly importing = signal(false);
  readonly lastImport = signal<ImportResult | null>(null);
  readonly batches = signal<ImportBatch[]>([]);

  readonly detail = signal<Detail | null>(null);

  readonly rejectionsBatch = signal<number | null>(null);
  readonly rejections = signal<Rejection[]>([]);

  readonly scenarioStep = signal<number | null>(null); // null when the dev scenario API isn't available
  readonly scenarioMax = signal(0);

  ngOnInit(): void {
    this.loadBatches();
    this.api.scenario().subscribe({
      next: (s) => {
        this.scenarioStep.set(s.step);
        this.scenarioMax.set(s.maxStep);
      },
      error: () => this.scenarioStep.set(null),
    });
  }

  runImport(): void {
    this.importing.set(true);
    this.api.runImport().subscribe({
      next: (result) => {
        this.lastImport.set(result);
        this.importing.set(false);
        this.error.set(null);
        this.grid().reload();
        this.loadBatches();
        const open = this.detail()?.transaction;
        if (open) this.openDetail(open);
      },
      error: (e) => {
        this.importing.set(false);
        this.error.set(errorMessage(e));
      },
    });
  }

  setScenario(step: string): void {
    this.api.setScenario(Number(step)).subscribe({
      next: (s) => this.scenarioStep.set(s.step),
      error: (e) => this.error.set(errorMessage(e)),
    });
  }

  openDetail(transaction: Transaction): void {
    this.api.detail(transaction.sourceSystem, transaction.transactionId).subscribe({
      next: (d) => this.detail.set(d),
      error: (e) => this.error.set(errorMessage(e)),
    });
  }

  closeDetail(): void {
    this.detail.set(null);
  }

  // clicking the same batch again hides the list
  toggleRejections(batchId: number): void {
    if (this.rejectionsBatch() === batchId) {
      this.rejectionsBatch.set(null);
      return;
    }
    this.api.rejections(batchId).subscribe({
      next: (list) => {
        this.rejections.set(list);
        this.rejectionsBatch.set(batchId);
      },
      error: (e) => this.error.set(errorMessage(e)),
    });
  }

  // 0..max, for the scenario dropdown
  scenarioSteps(): number[] {
    return Array.from({ length: this.scenarioMax() + 1 }, (_, i) => i);
  }

  private loadBatches(): void {
    // the recent imports list is secondary, so a failure here is ignored
    this.api.imports().subscribe({ next: (b) => this.batches.set(b), error: () => {} });
  }
}
