/** Signs out with a POST to /api/auth/logout (clears the session and signs out of Microsoft). */
export default function SignOutButton({ className }: { className?: string }) {
  return (
    <form action="/api/auth/logout" method="post">
      <button type="submit" className={className}>
        Sign out
      </button>
    </form>
  );
}
