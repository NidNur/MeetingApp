import { toLocalDateTimeInput, toUtcIso } from './meeting-dates';

describe('Meeting date conversion', () => {
  it('converts a browser-local minute to its actual UTC instant with a Z suffix', () => {
    const local = new Date(2026, 10, 10, 12, 15, 0, 0);
    expect(toUtcIso('2026-11-10T12:15')).toBe(local.toISOString());
    expect(toUtcIso('2026-11-10T12:15')).toMatch(/Z$/);
  });

  it('formats the instant in the browser timezone instead of slicing the UTC text', () => {
    const local = new Date(2026, 10, 10, 12, 15, 37, 123);
    expect(toLocalDateTimeInput(local.toISOString())).toBe('2026-11-10T12:15');
  });

  it('roundtrips a UTC instant at minute precision without adding a fixed timezone offset', () => {
    const original = new Date(2026, 6, 15, 12, 45, 0, 0).toISOString();
    expect(toUtcIso(toLocalDateTimeInput(original))).toBe(original);
  });

  it('keeps the local calendar day correct across the UTC day boundary', () => {
    for (const local of [new Date(2026, 0, 1, 0, 5), new Date(2026, 11, 31, 23, 55)]) {
      const expected = local.getMonth() === 0 ? '2026-01-01T00:05' : '2026-12-31T23:55';
      expect(toLocalDateTimeInput(local.toISOString())).toBe(expected);
      expect(toUtcIso(expected)).toBe(local.toISOString());
    }
  });

  for (const invalid of [
    '', '2026-11-10', '2026-11-10 12:15', '2026-1-10T12:15',
    '2026-11-10T12:15Z', '2026-11-10T12:15:00', '2026-11-10T12:15+03:00',
    ' 2026-11-10T12:15', '2026-11-10T12:15 ',
    '2026-00-10T12:15', '2026-13-10T12:15', '2026-01-00T12:15',
    '2026-04-31T12:15', '2026-02-29T12:15', '1900-02-29T12:15',
    '2026-11-10T24:00', '2026-11-10T12:60', '0000-06-01T12:15'
  ]) {
    it(`rejects an invalid or noncanonical local value: ${JSON.stringify(invalid)}`, () => {
      expect(toUtcIso(invalid)).toBeNull();
    });
  }

  it('accepts valid leap days, including the divisible-by-400 century rule', () => {
    for (const local of ['2024-02-29T12:15', '2000-02-29T12:15', '0004-02-29T12:15']) {
      const iso = toUtcIso(local);
      expect(iso).not.toBeNull();
      expect(toLocalDateTimeInput(iso!)).toBe(local);
    }
  });

  it('preserves years 0001–0099 instead of silently moving them into the 1900s', () => {
    for (const local of ['0001-06-01T12:15', '0099-06-01T12:15']) {
      const iso = toUtcIso(local);
      expect(iso).not.toBeNull();
      expect(new Date(iso!).getFullYear()).toBe(Number(local.slice(0, 4)));
      expect(toLocalDateTimeInput(iso!)).toBe(local);
    }
  });

  it('returns an empty display value for an unparseable instant', () => {
    expect(toLocalDateTimeInput('not a timestamp')).toBe('');
    expect(toLocalDateTimeInput('')).toBe('');
  });

  it('rejects a nonexistent local minute during a real forward timezone transition', () => {
    // Discover a transition in the browser zone so the assertion also works in Istanbul,
    // Europe, North America and southern-hemisphere zones without assuming an offset.
    const day = 24 * 60 * 60 * 1000;
    const minute = 60 * 1000;
    let previousTime = Date.UTC(2010, 0, 1);
    let previousOffset = new Date(previousTime).getTimezoneOffset();
    for (let time = previousTime + day; time <= Date.UTC(2031, 0, 1); time += day) {
      const offset = new Date(time).getTimezoneOffset();
      if (offset < previousOffset) {
        let low = previousTime;
        let high = time;
        while (high - low > minute) {
          const middle = Math.floor((low + high) / (2 * minute)) * minute;
          if (new Date(middle).getTimezoneOffset() === previousOffset) low = middle;
          else high = middle;
        }
        const before = new Date(high - minute);
        const missingMinute = new Date(Date.UTC(before.getFullYear(), before.getMonth(), before.getDate(),
          before.getHours(), before.getMinutes() + 1)).toISOString().slice(0, 16);
        expect(toUtcIso(missingMinute)).toBeNull();
        return;
      }
      previousTime = time;
      previousOffset = offset;
    }
    pending('The browser timezone has no forward transition from 2010 through 2030.');
  });
});
