// Match .NET String.Trim for non-control whitespace. Controls are rejected before trimming.
export function trimEventTypeName(value: string): string {
  return value.replace(
    /^[ \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+|[ \u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000]+$/g,
    '',
  );
}
export function validEventTypeName(value: string): boolean {
  const trimmed = trimEventTypeName(value);
  return (
    !/[\u0000-\u001f\u007f-\u009f]/.test(value) && trimmed.length >= 1 && trimmed.length <= 100
  );
}
