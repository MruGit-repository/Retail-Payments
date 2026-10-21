import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type TransactionStatus = 'Active' | 'Revoked' | 'Finalized';

export interface Transaction {
  id: number;
  sourceSystem: string;
  transactionId: number;
  cardNumber: string;
  locationCode: string;
  productName: string;
  amount: number;
  transactionTime: string;
  status: TransactionStatus;
  revision: number;
  firstImportedAt: string;
  lastUpdatedAt: string;
}

export interface Audit {
  id: number;
  importBatchId: number;
  changeType: string;
  fieldName: string | null;
  oldValue: string | null;
  newValue: string | null;
  revision: number;
  occurredAt: string;
}

export interface TransactionDetail {
  transaction: Transaction;
  audit: Audit[];
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Counts {
  created: number;
  updated: number;
  revoked: number;
  restored: number;
  finalized: number;
  unchanged: number;
  rejected: number;
}

export interface SourceResult {
  sourceSystem: string;
  succeeded: boolean;
  error: string | null;
  received: number;
  counts: Counts;
}

export interface ImportResult {
  batchId: number;
  status: string;
  triggeredBy: string;
  totals: Counts;
  sources: SourceResult[];
}

export interface ImportBatch extends Counts {
  id: number;
  startedAt: string;
  completedAt: string | null;
  status: string;
  triggeredBy: string;
  error: string | null;
}

export interface Rejection {
  id: number;
  importBatchId: number;
  sourceSystem: string;
  transactionId: number;
  reason: string;
  payload: string;
  rejectedAt: string;
}

export interface TransactionQuery {
  sourceSystem: string;
  status: string;
  search: string;
  sortBy: string;
  sortDir: 'asc' | 'desc';
  page: number;
  pageSize: number;
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);

  transactions(q: TransactionQuery): Observable<Paged<Transaction>> {
    let params = new HttpParams()
      .set('sortBy', q.sortBy)
      .set('sortDir', q.sortDir)
      .set('page', q.page)
      .set('pageSize', q.pageSize);
    if (q.sourceSystem) params = params.set('sourceSystem', q.sourceSystem);
    if (q.status) params = params.set('status', q.status);
    if (q.search.trim()) params = params.set('search', q.search.trim());
    return this.http.get<Paged<Transaction>>('/api/transactions', { params });
  }

  detail(source: string, id: number): Observable<TransactionDetail> {
    return this.http.get<TransactionDetail>(`/api/transactions/${encodeURIComponent(source)}/${id}`);
  }

  runImport(): Observable<ImportResult> {
    return this.http.post<ImportResult>('/api/import', null);
  }

  imports(): Observable<ImportBatch[]> {
    return this.http.get<ImportBatch[]>('/api/imports?take=5');
  }

  rejections(batchId: number): Observable<Rejection[]> {
    return this.http.get<Rejection[]>(`/api/imports/${batchId}/rejections`);
  }

  scenario(): Observable<{ step: number; maxStep: number }> {
    return this.http.get<{ step: number; maxStep: number }>('/api/dev/scenario');
  }

  setScenario(step: number): Observable<{ step: number; maxStep: number }> {
    return this.http.post<{ step: number; maxStep: number }>(`/api/dev/scenario/${step}`, null);
  }
}
