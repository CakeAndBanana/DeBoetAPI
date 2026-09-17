'use client';

import { useTransition } from 'react';
import { useRouter } from 'next/navigation';
import {
  Button, Card, Group, Stack, Switch, Textarea, TextInput,
} from '@mantine/core';
import { useForm } from '@mantine/form';
import { notifications } from '@mantine/notifications';
import { inactiveAbove, type CategoryOption } from '@/lib/categories';
import { CategoryPicker } from '@/components/category-picker';
import { useT } from '@/i18n/client';
import { createCategory, updateCategory, type CategoryInput } from './actions';

interface Props {
  options: CategoryOption[];
  /** Present when editing. */
  id?: string;
  initial?: Partial<CategoryInput>;
  /**
   * Supplied when the form lives in a modal: there is no page to leave afterwards, so saving
   * and cancelling close the dialog instead of navigating. Its presence is also what drops the
   * card around the fields, since the dialog is already one.
   */
  onClose?: () => void;
}

export function CategoryForm({ options, id, initial, onClose }: Props) {
  const router = useRouter();
  const t = useT();
  const [pending, startTransition] = useTransition();

  const form = useForm<CategoryInput>({
    initialValues: {
      name: initial?.name ?? '',
      slug: initial?.slug ?? '',
      redirectToId: initial?.redirectToId ?? null,
      metaTitle: initial?.metaTitle ?? '',
      metaDescription: initial?.metaDescription ?? '',
      parentId: initial?.parentId ?? null,
      // Not shown, but still submitted: the API's SortOrder has no default, so leaving it
      // out of the payload would silently reset an existing category's ordering to 0.
      sortOrder: initial?.sortOrder ?? 0,
      // A new category under a section that is switched off starts switched off as well. The
      // API refuses the other combination, and arriving at a form that cannot be saved until
      // you find the right switch is not what "add one here" should mean.
      isActive: initial?.isActive ?? inactiveAbove(options, initial?.parentId) === null,
      isMenu: initial?.isMenu ?? true,
    },
    validate: { name: (v) => (v.trim() ? null : t('common.required')) },
  });

  /**
   * The parent this category hangs from: the one being picked on a new category, or the one it
   * already sits under when editing, since the edit form does not offer to change it.
   */
  const parentId = id
    ? options.find((o) => o.value === id)?.parentId ?? null
    : form.values.parentId ?? null;

  /**
   * The switched-off category above this one, if there is one. Naming it is the point: it can be
   * several levels up, and "why will this not switch on" needs an answer in the form rather than
   * a refusal from the API after saving.
   */
  const switchedOffAbove = inactiveAbove(options, parentId);

  function leave() {
    if (onClose) onClose();
    else router.push('/categories');
  }

  function submit(values: CategoryInput) {
    startTransition(async () => {
      // The slug travels with the payload now. On create the field is not rendered, so it
      // is blank and the API derives one from the name; on edit it carries whatever is in the
      // box, which means renaming no longer silently rewrites an established route.

      // The active flag is forced rather than trusted: the switch below is disabled in this
      // state, and a value left over from before the parent was picked would only earn a
      // refusal from the API.
      const payload = switchedOffAbove ? { ...values, isActive: false } : values;

      const result = id ? await updateCategory(id, payload) : await createCategory(payload);

      if (!result.ok) {
        notifications.show({ color: 'red', title: t('common.refused'), message: result.message! });
        return;
      }

      notifications.show({ color: 'green', title: t('common.saved'), message: t('categories.savedMessage') });
      leave();
      // Refreshed either way: the tree behind the modal is a Server Component, so without this
      // the category that was just created would not appear until the next navigation.
      router.refresh();
    });
  }

  const body = (
    <form onSubmit={form.onSubmit(submit)}>
      <Stack>
        <TextInput label={t('categories.fieldName')} withAsterisk {...form.getInputProps('name')} />

        {/*
          Optional on both forms. Left alone it derives from the name, but a route may need
          to be spelled a particular way — including with hyphens, which the derived form
          never produces — and that has to be correctable before the category exists as well
          as after, since a duplicate route is refused at creation.
        */}
        <TextInput
          label={t('categories.fieldSlug')}
          placeholder={id ? undefined : t('categories.fieldSlugPlaceholder')}
          description={t('categories.fieldSlugHelp')}
          {...form.getInputProps('slug')}
        />

        {!id && (
          <CategoryPicker
            label={t('categories.fieldParent')}
            placeholder={t('categories.fieldParentPlaceholder')}
            description={t('categories.fieldParentHelp')}
            options={options}
            {...form.getInputProps('parentId')}
            onChange={(value) => {
              form.setFieldValue('parentId', value);

              // Picking a section that is switched off switches this one off with it, rather
              // than leaving a switch on that the save would refuse.
              if (inactiveAbove(options, value)) form.setFieldValue('isActive', false);
            }}
          />
        )}

        <TextInput
          label={t('categories.fieldMetaTitle')}
          description={t('categories.fieldMetaTitleHelp')}
          {...form.getInputProps('metaTitle')}
        />
        {/*
          A category that forwards has no page of its own, so this sits with the fields that
          decide how it is published rather than with the SEO text.
        */}
        <CategoryPicker
          label={t('categories.fieldRedirect')}
          placeholder={t('categories.fieldRedirectPlaceholder')}
          description={t('categories.fieldRedirectHelp')}
          options={options}
          exclude={id ? new Set([id]) : undefined}
          {...form.getInputProps('redirectToId')}
        />

        <Textarea
          label={t('categories.fieldMetaDesc')}
          description={t('categories.fieldMetaDescHelp')}
          autosize
          minRows={2}
          {...form.getInputProps('metaDescription')}
        />

        <Group>
          <Switch
            label={t('categories.fieldActive')}
            description={switchedOffAbove
              ? t('categories.fieldActiveBlocked', { name: switchedOffAbove.name })
              : t('categories.fieldActiveHelp')}
            disabled={!!switchedOffAbove}
            {...form.getInputProps('isActive', { type: 'checkbox' })}
            checked={switchedOffAbove ? false : form.values.isActive}
          />
          <Switch
            label={t('categories.fieldMenu')}
            description={t('categories.fieldMenuHelp')}
            {...form.getInputProps('isMenu', { type: 'checkbox' })}
          />
        </Group>

        <Group>
          <Button type="submit" loading={pending}>{t('common.save')}</Button>
          {/* type="button": Mantine sets none, and a bare button in a form submits it — so
              cancelling was also saving. */}
          <Button type="button" variant="subtle" onClick={leave}>{t('common.cancel')}</Button>
        </Group>
      </Stack>
    </form>
  );

  return onClose ? body : <Card withBorder maw={640}>{body}</Card>;
}
