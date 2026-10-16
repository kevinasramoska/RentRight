export function LoadingState() {
  return <p role="status">Loading your session…</p>;
}
export function ErrorState({ onRetry }: { onRetry: () => void }) {
  return <div role="alert"><p>We couldn’t load your session. Please try again.</p><button onClick={onRetry}>Try again</button></div>;
}
export function EmptyState({ message }: { message: string }) {
  return <p>{message}</p>;
}
