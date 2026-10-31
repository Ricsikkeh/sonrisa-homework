import {
  destinationOptions,
  emptyDestinationDraft,
  isDestinationSettings,
  normalizeDestinations,
  sameDestinations,
  toDestinationDraft,
  validDestinations,
} from './destination-options';

describe('Destination catalog', () => {
  it('has unique stable channel IDs and API fields', () => {
    expect(destinationOptions.map(({ channel }) => channel)).toEqual(['email', 'slack']);
    expect(new Set(destinationOptions.map(({ field }) => field)).size).toBe(
      destinationOptions.length,
    );
  });

  it('creates, normalizes and restores drafts for every option', () => {
    const empty = emptyDestinationDraft();
    for (const option of destinationOptions) expect(empty[option.field]).toBe('');
    const settings = normalizeDestinations({ email: '  alice@example.com  ', slackLabel: ' \t ' });
    expect(settings).toEqual({ email: 'alice@example.com', slackLabel: null });
    expect(toDestinationDraft(settings)).toEqual({ email: 'alice@example.com', slackLabel: '' });
    expect(sameDestinations(settings, { ...settings })).toBe(true);
    for (const option of destinationOptions) {
      expect(sameDestinations(settings, { ...settings, [option.field]: 'different' })).toBe(false);
    }
  });

  it('applies each option policy, including exact limits and optional values', () => {
    expect(validDestinations({ email: null, slackLabel: null })).toBe(true);
    expect(
      validDestinations({ email: `${'a'.repeat(242)}@example.com`, slackLabel: 's'.repeat(100) }),
    ).toBe(true);
    for (const settings of [
      { email: `${'a'.repeat(243)}@example.com`, slackLabel: null },
      { email: 'not-an-email', slackLabel: null },
      { email: 'Name <alice@example.com>', slackLabel: null },
      { email: 'alice\u007f@example.com', slackLabel: null },
      { email: null, slackLabel: 's'.repeat(101) },
      { email: null, slackLabel: '#bad\u0085label' },
    ])
      expect(validDestinations(settings)).toBe(false);
  });

  it('rejects malformed or noncanonical responses consistently for both consumers', () => {
    expect(isDestinationSettings({ email: null, slackLabel: '#weather' })).toBe(true);
    for (const response of [
      null,
      [],
      {},
      { email: null },
      { email: 42, slackLabel: null },
      { email: '', slackLabel: null },
      { email: null, slackLabel: ' ' },
      { email: ' alice@example.com', slackLabel: null },
      { email: null, slackLabel: '#bad\nlabel' },
    ])
      expect(isDestinationSettings(response)).toBe(false);
  });
});
