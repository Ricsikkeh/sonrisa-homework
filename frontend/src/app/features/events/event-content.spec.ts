import { trimEventText, validTitle, validMessage, validUuid } from './event-content';

describe('event content validation', () => {
  describe('non-applicable values', () => {
    it('does not apply UUID validation to non-string values', () => {
      expect(validUuid(null)).toBe(false);
      expect(validUuid(undefined)).toBe(false);
      expect(validUuid(42)).toBe(false);
    });
  });

  describe('titles', () => {
    it('rejects blank values and control characters, including outer newlines and C1 U+0085', () => {
      for (const value of ['', ' \t\r\n ', '\nTitle', 'Title\n', 'Title\u0085']) {
        expect(validTitle(value), JSON.stringify(value)).toBe(false);
      }
    });

    it('allows at most 200 UTF-16 code units (100 emoji), not 101 emoji', () => {
      expect(validTitle('😀'.repeat(100))).toBe(true);
      expect(validTitle('😀'.repeat(101))).toBe(false);
    });

    it('trims .NET whitespace including U+3000 but preserves a non-whitespace BOM', () => {
      expect(trimEventText('\u3000Title\u3000')).toBe('Title');
      expect(validTitle('\u3000Title\u3000')).toBe(true);
      expect(trimEventText('\uFEFFTitle\uFEFF')).toBe('\uFEFFTitle\uFEFF');
      expect(validTitle('\uFEFFTitle\uFEFF')).toBe(true);
      expect(validTitle('\uFEFF')).toBe(true);
    });
  });

  describe('messages', () => {
    it('allows 4000 UTF-16 code units and rejects 4001', () => {
      expect(validMessage('a'.repeat(4000))).toBe(true);
      expect(validMessage('a'.repeat(4001))).toBe(false);
      expect(validMessage('😀'.repeat(2000))).toBe(true);
      expect(validMessage('😀'.repeat(2001))).toBe(false);
      expect(validMessage(' \r\n\t ')).toBe(false);
    });

    it('allows multiline CRLF and TAB and preserves interior whitespace', () => {
      const message = '  first\r\n\t second  ';
      expect(validMessage(message)).toBe(true);
      expect(trimEventText(message)).toBe('first\r\n\t second');
    });

    it('trims .NET whitespace including U+3000 while not trimming BOM', () => {
      expect(trimEventText('\u3000Message\u3000')).toBe('Message');
      expect(validMessage('\u3000Message\u3000')).toBe(true);
      expect(trimEventText('\uFEFFMessage\uFEFF')).toBe('\uFEFFMessage\uFEFF');
      expect(validMessage('\uFEFF')).toBe(true);
    });

    it('rejects forbidden vertical tab, form feed, and NUL controls', () => {
      for (const value of ['a\u000b', 'a\u000c', 'a\u0000']) {
        expect(validMessage(value), JSON.stringify(value)).toBe(false);
      }
    });
  });
});
