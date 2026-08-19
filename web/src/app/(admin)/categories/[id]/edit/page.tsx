import { notFound } from 'next/navigation';
import { Stack, Title } from '@mantine/core';
import { ApiError } from '@/lib/problem';
import { flatten, getCategory, getTree } from '@/lib/categories';
import { CategoryForm } from '../../category-form';

export default async function EditCategoryPage({
  params,
}: {
  params: Promise<{ id: string }>;
}) {
  const { id } = await params;

  const detail = await getCategory(id).catch((error) => {
    if (error instanceof ApiError && error.status === 404) notFound();
    throw error;
  });

  const options = flatten(await getTree());

  return (
    <Stack>
      <Title order={2}>Edit {detail.category.name}</Title>
      <CategoryForm
        id={id}
        options={options}
        hasChildren={detail.children.length > 0}
        initial={{
          name: detail.category.name,
          metaTitle: detail.category.metaTitle,
          metaDescription: detail.category.metaDescription ?? '',
          // The spec types integers as number|string because the API accepts either on the
          // way in. Responses are always numbers, so this normalises rather than trusting it.
          sortOrder: Number(detail.category.sortOrder),
          isActive: detail.category.isActive,
          isMenu: detail.category.isMenu,
        }}
      />
    </Stack>
  );
}
