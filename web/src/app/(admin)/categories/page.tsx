import { Stack, Text, Title } from '@mantine/core';
import { flatten, getTree } from '@/lib/categories';
import { CategoryTree } from './category-tree';

export default async function CategoriesPage() {
  const tree = await getTree();
  const options = flatten(tree);

  return (
    <Stack>
      <div>
        <Title order={2}>Categories</Title>
        <Text c="dimmed" size="sm">
          One main parent sets each category&apos;s route; secondary links let it appear
          elsewhere without being duplicated.
        </Text>
      </div>

      <CategoryTree nodes={tree} options={options} />
    </Stack>
  );
}
