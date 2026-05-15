export function errorMessage(err: unknown): string {
  if (err && typeof err === "object") {
    const e = err as Record<string, unknown>;
    if (typeof e.error === "string") return e.error;
    if (typeof e.detail === "string") return e.detail;
    if (typeof e.title === "string") return e.title;
    if (typeof e.message === "string") return e.message;
  }
  if (err instanceof Error) return err.message;
  return String(err);
}
