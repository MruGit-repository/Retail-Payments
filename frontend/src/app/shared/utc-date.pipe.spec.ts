import { UtcDatePipe } from './utc-date.pipe';

describe('UtcDatePipe', () => {
  const pipe = new UtcDatePipe();

  it('formats in UTC regardless of the local time zone', () => {
    expect(pipe.transform('2026-02-27T11:20:05Z')).toBe('2026-02-27 11:20:05');
  });

  it('accepts a custom format', () => {
    expect(pipe.transform('2026-02-27T11:20:05Z', 'MM-dd HH:mm')).toBe('02-27 11:20');
  });

  it('returns an empty string for missing values', () => {
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });
});
