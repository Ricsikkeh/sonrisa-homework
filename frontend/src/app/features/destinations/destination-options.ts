const controls = /[\u0000-\u001f\u007f-\u009f]/u;

/** Stable channel IDs and API field names; changing either is a contract change. */
export const destinationOptions = [
  {
    channel: 'email',
    field: 'email',
    label: 'Email',
    fieldLabel: 'Email address (optional)',
    inputType: 'email',
    autocomplete: 'email',
    maxLength: 254,
    hint: 'Up to 254 characters. No email verification or real delivery.',
    validationMessage:
      'Use a valid email address of up to 254 characters, without whitespace or control characters.',
    validValue: (value: string) => !controls.test(value) && /^[^@\s<>]+@[^@\s<>]+$/u.test(value),
  },
  {
    channel: 'slack',
    field: 'slackLabel',
    label: 'Slack',
    fieldLabel: 'Simulated Slack destination label (optional)',
    inputType: 'text',
    autocomplete: 'off',
    maxLength: 100,
    hint: 'Up to 100 characters, for example #weather-alerts. Not a webhook or token.',
    validationMessage: 'Use a Slack label of up to 100 characters, without control characters.',
    validValue: (value: string) => !controls.test(value),
  },
] as const;

export type DestinationOption = (typeof destinationOptions)[number];
export type DestinationChannel = DestinationOption['channel'];
export type DestinationField = DestinationOption['field'];
export type DestinationSettings = Record<DestinationField, string | null>;
export type DestinationDraft = Record<DestinationField, string>;

export function emptyDestinationDraft(): DestinationDraft {
  return Object.fromEntries(destinationOptions.map(({ field }) => [field, ''])) as DestinationDraft;
}

export function toDestinationDraft(settings: DestinationSettings): DestinationDraft {
  return Object.fromEntries(
    destinationOptions.map(({ field }) => [field, settings[field] ?? '']),
  ) as DestinationDraft;
}

export function normalizeDestinations(draft: DestinationDraft): DestinationSettings {
  return Object.fromEntries(
    destinationOptions.map(({ field }) => [field, draft[field].trim() || null]),
  ) as DestinationSettings;
}

export function validDestinationValue(option: DestinationOption, value: string | null): boolean {
  return value === null || (value.length <= option.maxLength && option.validValue(value));
}

export function validDestinations(settings: DestinationSettings): boolean {
  return destinationOptions.every((option) =>
    validDestinationValue(option, settings[option.field]),
  );
}

/** Fail closed on missing fields or noncanonical server values, in both consuming features. */
export function isDestinationSettings(value: unknown): value is DestinationSettings {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return false;
  const item = value as Partial<DestinationSettings>;
  return destinationOptions.every((option) => {
    const setting = item[option.field];
    return (
      setting === null ||
      (typeof setting === 'string' &&
        setting.length > 0 &&
        setting === setting.trim() &&
        validDestinationValue(option, setting))
    );
  });
}

export function sameDestinations(left: DestinationSettings, right: DestinationSettings): boolean {
  return destinationOptions.every(({ field }) => left[field] === right[field]);
}
