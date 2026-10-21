import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { App } from './app';

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
  });

  it('renders transactions returned by the API', async () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();

    http.expectOne((r) => r.url === '/api/transactions').flush({
      items: [
        {
          id: 1, sourceSystem: 'Online', transactionId: 100001, cardNumber: '***4444',
          locationCode: 'ONLINE', productName: 'USB-C Docking Station', amount: 129.95,
          transactionTime: '2026-02-27T11:20:05Z', status: 'Active', revision: 1,
          firstImportedAt: '2026-02-27T12:00:00Z', lastUpdatedAt: '2026-02-27T12:00:00Z',
        },
      ],
      total: 1, page: 1, pageSize: 10,
    });
    http.match(() => true).forEach((r) => r.flush([])); // the imports and scenario calls
    await fixture.whenStable();
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('USB-C Docking Station');
    expect(text).toContain('129.95');
    expect(text).toContain('Active');
  });
});
