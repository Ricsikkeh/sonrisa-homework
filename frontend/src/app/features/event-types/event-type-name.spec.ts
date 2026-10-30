import { trimEventTypeName, validEventTypeName } from './event-type-name';

describe('Event type name invariants', () => {
  it('uses the backend whitespace policy, preserving FEFF and Unicode spelling', () => {
    expect(trimEventTypeName('\u00a0Événement\u3000')).toBe('Événement');
    expect(trimEventTypeName('\ufeffWeather\ufeff')).toBe('\ufeffWeather\ufeff');
    expect(validEventTypeName('🌍'.repeat(50))).toBe(true);
    expect(validEventTypeName('🌍'.repeat(51))).toBe(false);
  });
  it('rejects control characters even when .NET would otherwise trim them', () => {
    for (const name of ['\tName', 'Name\n', 'Name\u0085', 'Name\u007f'])
      expect(validEventTypeName(name)).toBe(false);
  });
});
