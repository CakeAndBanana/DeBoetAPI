'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { NavLink } from '@mantine/core';

/**
 * Client component on purpose: Mantine's NavLink is a client component, and React refuses to
 * let a Server Component pass it a function prop like `component={Link}`.
 */
export function AdminNav({ isAdmin }: { isAdmin: boolean }) {
  const pathname = usePathname();

  return (
    <>
      <NavLink
        component={Link}
        href="/categories"
        label="Categories"
        active={pathname.startsWith('/categories')}
      />
      {/* Presentation only — the API refuses editors regardless of what is rendered. */}
      {isAdmin && (
        <>
          <NavLink
            component={Link}
            href="/users"
            label="Users"
            active={pathname.startsWith('/users')}
          />
          <NavLink
            component={Link}
            href="/audit"
            label="Change history"
            active={pathname.startsWith('/audit')}
          />
        </>
      )}
    </>
  );
}
