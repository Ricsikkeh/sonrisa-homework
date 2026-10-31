// Match .NET String.Trim, including its control whitespace (not JavaScript's BOM trimming).
export function trimEventText(value: string): string {
  return value.replace(
    /^[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[\u0009-\u000d\u0020\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$/g,
    '',
  );
}
export function validTitle(value: string): boolean {
  const text = trimEventText(value);
  return !/[\u0000-\u001f\u007f-\u009f]/.test(value) && text.length >= 1 && text.length <= 200;
}
export function validMessage(value: string): boolean {
  const text = trimEventText(value);
  return (
    !/[\u0000-\u0008\u000b\u000c\u000e-\u001f\u007f-\u009f]/.test(value) &&
    text.length >= 1 &&
    text.length <= 4000
  );
}
export function validUuid(value: unknown): value is string {
  return (
    typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) &&
    value !== '00000000-0000-0000-0000-000000000000'
  );
}
