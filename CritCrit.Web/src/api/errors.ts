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

export function supportId(err: unknown): string | null {
  if (!err || typeof err !== "object") return null;
  const e = err as Record<string, unknown>;
  if (typeof e.supportId === "string") return e.supportId;
  if (typeof e.traceId === "string") return e.traceId;
  return null;
}

export function errorDescription(err: unknown): string {
  const id = supportId(err);
  return id ? `${errorMessage(err)} · support ${id}` : errorMessage(err);
}
