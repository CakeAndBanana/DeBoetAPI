'use client';

import { useTransition } from 'react';
import { Select } from '@mantine/core';
import { IconLanguage } from '@tabler/icons-react';
import { LOCALES, type Locale } from '@/i18n';
import { useLocale, useT } from '@/i18n/client';
import { setLocale } from '@/i18n/locale-action';

/**
 * Shared rather than part of the admin shell, because the login screen needs it too: someone
 * who cannot read the sign-in form is exactly the person who needs to change the language, and
 * on that page there is no shell to put it in.
 */
export function LanguageSwitcher() {
  const locale = useLocale();
  const t = useT();
  const [pending, startTransition] = useTransition();

  return (
    <Select
      size="xs"
      w={140}
      aria-label={t('app.language')}
      leftSection={<IconLanguage size={14} />}
      allowDeselect={false}
      value={locale}
      disabled={pending}
      data={Object.entries(LOCALES).map(([value, { label }]) => ({ value, label }))}
      // A Server Action, because the choice lives in a cookie and only the server may set one.
      // router.refresh() inside the action's revalidation re-renders every Server Component
      // with the new dictionary.
      onChange={(next) => next && startTransition(() => setLocale(next as Locale))}
    />
  );
}
