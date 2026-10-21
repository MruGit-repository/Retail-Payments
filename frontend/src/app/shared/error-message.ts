import { HttpErrorResponse } from '@angular/common/http';

// text for the error banner
export function errorMessage(error: unknown): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 0) return 'Cannot reach the API. Is the backend running?';
    return error.error?.title ?? error.message;
  }
  return 'Unexpected error';
}
