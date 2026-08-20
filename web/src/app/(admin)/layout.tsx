import { getSession, isAdmin } from '@/lib/api';
import { AdminShell } from './admin-shell';

/**
 * Kept as a Server Component so the signed-in name comes from the sealed session without a
 * round trip. The frame itself is a Client Component, because the mobile navbar has open
 * state; only plain data crosses the boundary.
 */
export default async function AdminLayout({ children }: { children: React.ReactNode }) {
  const session = await getSession();

  return (
    <AdminShell
      displayName={session.displayName}
      roles={session.roles}
      isAdmin={isAdmin(session)}
    >
      {children}
    </AdminShell>
  );
}
