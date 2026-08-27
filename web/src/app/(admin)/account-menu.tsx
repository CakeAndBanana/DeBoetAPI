'use client';

import Link from 'next/link';
import { usePathname } from 'next/navigation';
import { NavLink } from '@mantine/core';
import { IconKey, IconUserCircle } from '@tabler/icons-react';
import { useT } from '@/i18n/client';

/**
 * Who you are signed in as, and what you can do about it.
 *
 * The name is the control rather than a label: pressing it opens onto password management,
 * which is the only account setting there is. Kept collapsed by default so the menu reads as
 * navigation first — unless you are already on that page, in which case hiding the item that
 * got you there would be perverse.
 */
export function AccountMenu({
  displayName, roles, onNavigate,
}: {
  displayName: string | undefined;
  roles: string[] | undefined;
  onNavigate?: () => void;
}) {
  const pathname = usePathname();
  const t = useT();
  const onPasswordPage = pathname.startsWith('/change-password');

  return (
    <NavLink
      label={displayName ?? t('nav.signedIn')}
      // The roles as they actually are. Printing "editor" for anything that is not admin
      // would misreport a session whose roles have not been read yet.
      description={roles?.length ? roles.join(', ').toLowerCase() : undefined}
      leftSection={<IconUserCircle size={20} />}
      defaultOpened={onPasswordPage}
      childrenOffset={28}
    >
      <NavLink
        component={Link}
        href="/change-password"
        onClick={onNavigate}
        label={t('nav.password')}
        leftSection={<IconKey size={18} />}
        active={onPasswordPage}
      />
    </NavLink>
  );
}
