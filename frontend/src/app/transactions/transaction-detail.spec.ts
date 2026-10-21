import { TestBed } from '@angular/core/testing';
import { TransactionDetail as Detail } from '../api';
import { TransactionDetail } from './transaction-detail';

const detail: Detail = {
  transaction: {
    id: 1, sourceSystem: 'Store Uptown', transactionId: 100002, cardNumber: '***3333',
    locationCode: 'UPTOWN', productName: 'USB Cable', amount: 11.49,
    transactionTime: '2026-02-27T11:20:05Z', status: 'Active', revision: 2,
    firstImportedAt: '2026-02-27T12:00:00Z', lastUpdatedAt: '2026-02-27T13:00:00Z',
  },
  audit: [
    { id: 1, importBatchId: 1, changeType: 'Created', fieldName: null, oldValue: null, newValue: 'Active', revision: 1, occurredAt: '2026-02-27T12:00:00Z' },
    { id: 2, importBatchId: 3, changeType: 'FieldChanged', fieldName: 'Amount', oldValue: '9.99', newValue: '11.49', revision: 2, occurredAt: '2026-02-27T13:00:00Z' },
  ],
};

describe('TransactionDetail', () => {
  function create() {
    const fixture = TestBed.createComponent(TransactionDetail);
    fixture.componentRef.setInput('detail', detail);
    fixture.detectChanges();
    return fixture;
  }

  it('shows the transaction and its audit history', () => {
    const text = (create().nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Store Uptown');
    expect(text).toContain('USB Cable');
    expect(text).toContain('Amount');
    expect(text).toContain('9.99');
    expect(text).toContain('11.49');
  });

  it('emits closed on Escape and on the close button', () => {
    const fixture = create();
    let closed = 0;
    fixture.componentInstance.closed.subscribe(() => closed++);

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('button.close')!.click();

    expect(closed).toBe(2);
  });
});
