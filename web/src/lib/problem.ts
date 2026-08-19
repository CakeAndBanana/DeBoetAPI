/** RFC 9457 ProblemDetails, which is what the API returns for every failure. */
export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly problem?: ProblemDetails,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * The API already writes readable messages ("That would place the category inside its own
 * subtree."), so they are surfaced as-is rather than being replaced with invented wording.
 * Validation problems arrive as a field->messages map and are flattened.
 */
export function messageFor(problem: ProblemDetails | undefined, status: number): string {
  if (problem?.errors) {
    const flattened = Object.values(problem.errors).flat();
    if (flattened.length > 0) return flattened.join(' ');
  }

  if (problem?.detail) return problem.detail;
  if (problem?.title) return problem.title;

  switch (status) {
    case 401: return 'Your session has expired. Please sign in again.';
    case 403: return 'That action needs an administrator account.';
    case 404: return 'Not found.';
    default: return `Request failed (${status}).`;
  }
}
