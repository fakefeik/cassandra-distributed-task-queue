// @flow
import React from 'react';
import $c from 'property-chain';
import { RouterLink, Modal, Input, Button } from 'ui';
import { RowStack, ColumnStack } from 'ui/layout';
import { withRouter } from 'react-router';
import { Loader } from 'ui';
import TasksTable from '../components/TaskTable/TaskTable';
import TasksPaginator from '../components/TasksPaginator/TasksPaginator';
import TaskQueueFilter from '../components/TaskQueueFilter/TaskQueueFilter';
import { withRemoteTaskQueueApi } from '../api/RemoteTaskQueueApiInjection';
import { takeLastAndRejectPrevious } from './PromiseUtils';
import { SuperUserAccessLevels } from '../../Domain/Globals';
import { getCurrentUserInfo } from '../../Domain/Globals';
import { createDefaultRemoteTaskQueueSearchRequest, isRemoteTaskQueueSearchRequestEmpty } from '../api/RemoteTaskQueueApi';
import { TaskStates } from '../Domain/TaskState';
import { SearchQuery, queryStringMapping } from '../../Commons/QueryStringMapping';
import CommonLayout from '../../Commons/Layouts';
import type { RemoteTaskQueueSearchRequest, RemoteTaskQueueSearchResults } from '../api/RemoteTaskQueueApi';
import type { IRemoteTaskQueueApi } from '../api/RemoteTaskQueueApi';
import type { QueryStringMapping } from '../../Commons/QueryStringMapping';
import type { RouterLocationDescriptor } from '../../Commons/DataTypes/Routing';
import numberToString from '../Domain/numberToString';

type TasksPageContainerProps = {
    searchQuery: string;
    router: any;
    remoteTaskQueueApi: IRemoteTaskQueueApi;
    results: ?RemoteTaskQueueSearchResults;
};

type TasksPageContainerState = {
    loading: boolean;
    request: RemoteTaskQueueSearchRequest;
    availableTaskNames: string[] | null;
    confirmMultipleModalOpened: boolean;
    modalType: 'Rerun' | 'Cancel';
    manyTaskConfirm: string;
};

// type Paging = {
//     from: ?number;
//     size: ?number;
// };

const mapping: QueryStringMapping<RemoteTaskQueueSearchRequest> = queryStringMapping()
    .mapToDateTimeRange(x => x.enqueueDateTimeRange, 'enqueue')
    .mapToString(x => x.queryString, 'q')
    .mapToStringArray(x => x.names, 'types')
    .mapToSet(x => x.taskState, 'states', TaskStates)
    .build();

const pagingMapping: QueryStringMapping<{ from: ?number; size: ?number }> = queryStringMapping()
    .mapToInteger(x => x.from, 'from')
    .mapToInteger(x => x.size, 'to')
    .build();

export function buildSearchQueryForRequest(request: RemoteTaskQueueSearchRequest): string {
    return mapping.stringify(request);
}

class TasksPageContainer extends React.Component {
    props: TasksPageContainerProps;
    state: TasksPageContainerState = {
        loading: false,
        request: createDefaultRemoteTaskQueueSearchRequest(),
        availableTaskNames: null,
        confirmMultipleModalOpened: false,
        modalType: 'Rerun',
        manyTaskConfirm: '',
    };
    searchTasks = takeLastAndRejectPrevious(
        this.props.remoteTaskQueueApi.search.bind(this.props.remoteTaskQueueApi)
    );

    isSearchRequestEmpty(searchQuery: ?string): boolean {
        const request = mapping.parse(searchQuery);
        return isRemoteTaskQueueSearchRequestEmpty(request);
    }

    getRequestBySearchQuery(searchQuery: ?string): RemoteTaskQueueSearchRequest {
        const request = mapping.parse(searchQuery);
        if (isRemoteTaskQueueSearchRequestEmpty(request)) {
            return createDefaultRemoteTaskQueueSearchRequest();
        }
        return request;
    }

    componentWillMount() {
        const { searchQuery, results } = this.props;
        const request = this.getRequestBySearchQuery(searchQuery);

        this.setState({ request: request });
        this.updateAvailableTaskNamesIfNeed();
        if (!this.isSearchRequestEmpty(searchQuery) && !results) {
            this.loadData(searchQuery, request);
        }
    }

    async updateAvailableTaskNamesIfNeed(): Promise<void> {
        if (this.state.availableTaskNames === null) {
            const availableTaskNames = await this.props.remoteTaskQueueApi.getAllTasksNames();
            this.setState({ availableTaskNames: availableTaskNames });
        }
    }

    componentWillReceiveProps(nextProps: TasksPageContainerProps) {
        const { searchQuery, results } = nextProps;
        const request = this.getRequestBySearchQuery(searchQuery);

        this.setState({ request: request });
        this.updateAvailableTaskNamesIfNeed();
        if (!this.isSearchRequestEmpty(searchQuery) && !results) {
            this.loadData(searchQuery, request);
        }
    }

    async loadData(searchQuery: ?string, request: RemoteTaskQueueSearchRequest): Promise<void> {
        const { from, size } = pagingMapping.parse(searchQuery);
        const { router } = this.props;

        this.setState({ loading: true });
        try {
            const results = await this.searchTasks(request, (from || 0), (size || 20));
            router.replace({
                pathname: '/AdminTools/Tasks',
                search: searchQuery,
                state: {
                    results: results,
                },
            });
        }
        finally {
            this.setState({ loading: false });
        }
    }

    handleSearch() {
        const { router } = this.props;
        const { request } = this.state;

        router.push({
            pathname: '/AdminTools/Tasks',
            search: SearchQuery.combine(
                mapping.stringify(request),
                pagingMapping.stringify({ from: 0, size: 20 })
            ),
            state: null,
        });
    }

    getTaskLocation(id: string): RouterLocationDescriptor {
        const { results, searchQuery } = this.props;
        const { request } = this.state;
        const { from, size } = pagingMapping.parse(searchQuery);

        return {
            pathname: `/AdminTools/Tasks/${id}`,
            state: {
                parentLocation: {
                    pathname: '/AdminTools/Tasks',
                    search: SearchQuery.combine(
                        mapping.stringify(request),
                        pagingMapping.stringify({ from: from, size: size })
                    ),
                    state: {
                        results: results,
                    },
                },
            },
        };
    }

    getNextPageLocation(): RouterLocationDescriptor | null {
        const { searchQuery, results } = this.props;
        const { from, size } = pagingMapping.parse(searchQuery);
        const request = this.getRequestBySearchQuery(searchQuery);

        if (!results) {
            return null;
        }
        if (from + size >= results.totalCount) {
            return null;
        }
        return {
            pathname: '/AdminTools/Tasks',
            search: SearchQuery.combine(
                mapping.stringify(request),
                pagingMapping.stringify({ from: (from || 0) + (size || 20), size: (size || 20) })
            ),
        };
    }

    getPrevPageLocation(): RouterLocationDescriptor | null {
        const { searchQuery } = this.props;
        const { from, size } = pagingMapping.parse(searchQuery);
        const request = this.getRequestBySearchQuery(searchQuery);

        if ((from || 0) === 0) {
            return null;
        }
        return {
            pathname: '/AdminTools/Tasks',
            search: SearchQuery.combine(
                mapping.stringify(request),
                pagingMapping.stringify({ from: Math.max(0, (from || 0) - (size || 20)), size: (size || 20) })
            ),
        };
    }

    async handleRerunTask(id: string): Promise<void> {
        const { remoteTaskQueueApi } = this.props;
        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.rerunTasks([id]);
        }
        finally {
            this.setState({ loading: false });
        }
    }

    async handleCancelTask(id: string): Promise<void> {
        const { remoteTaskQueueApi } = this.props;
        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.cancelTasks([id]);
        }
        finally {
            this.setState({ loading: false });
        }
    }

    async handleRerunAll(): Promise<void> {
        const { searchQuery, remoteTaskQueueApi } = this.props;
        const request = this.getRequestBySearchQuery(searchQuery);

        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.rerunTasksByRequest(request);
        }
        finally {
            this.setState({ loading: false });
        }
    }

    async handleCancelAll(): Promise<void> {
        const { searchQuery, remoteTaskQueueApi } = this.props;
        const request = this.getRequestBySearchQuery(searchQuery);

        this.setState({ loading: true });
        try {
            await remoteTaskQueueApi.cancelTasksByRequest(request);
        }
        finally {
            this.setState({ loading: false });
        }
    }


    renderModal(): React.Element<*> {
        const { results } = this.props;
        const { modalType, manyTaskConfirm } = this.state;
        const confirmedRegExp = /б.*л.*я/i;
        const counter = (results && results.totalCount) || 0;

        return (
            <Modal onClose={() => this.closeModal()} width={500} data-tid='ConfirmMultipleOperationModal'>
                <Modal.Header>
                    Нужно подтверждение
                </Modal.Header>
                <Modal.Body>
                    <ColumnStack gap={2}>
                        <ColumnStack.Fit>
                            <span data-tid='ModalText'>
                            {modalType === 'Rerun'
                                ? 'Уверен, что все эти таски надо перезапустить?'
                                : 'Уверен, что все эти таски надо остановить?'
                            }
                            </span>
                        </ColumnStack.Fit>
                    {counter > 100 && [
                        <ColumnStack.Fit key='text'>
                            Это действие может задеть больше 100 тасок, если это точно надо сделать,
                            то напиши прописью количество тасок (их { counter }):
                        </ColumnStack.Fit>,
                        <ColumnStack.Fit key='input'>
                            <Input
                                data-tid='ConfirmationInput'
                                value={manyTaskConfirm}
                                onChange={(e, val) => this.setState({ manyTaskConfirm: val })}
                            />
                        </ColumnStack.Fit>,
                    ]}
                    </ColumnStack>
                </Modal.Body>
                <Modal.Footer>
                    <RowStack gap={2}>
                        <RowStack.Fit>
                            {modalType === 'Rerun'
                                ? <Button
                                    data-tid='RerunButton'
                                    use='success'
                                    disabled={counter > 100 &&
                                        (!confirmedRegExp.test(manyTaskConfirm) &&
                                         manyTaskConfirm !== numberToString(counter))}
                                    onClick={() => {
                                        this.handleRerunAll();
                                        this.closeModal();
                                    }}>Перезапустить все</Button>
                                : <Button
                                    data-tid='CancelButton'
                                    use='danger'
                                    disabled={counter > 100 &&
                                        (!confirmedRegExp.test(manyTaskConfirm) &&
                                         manyTaskConfirm !== numberToString(counter))}
                                    onClick={() => {
                                        this.handleCancelAll();
                                        this.closeModal();
                                    }}>Остановить все</Button>
                            }
                        </RowStack.Fit>
                        <RowStack.Fit>
                            <Button data-tid='CloseButton' onClick={() => this.closeModal()}>Закрыть</Button>
                        </RowStack.Fit>
                    </RowStack>
                </Modal.Footer>
            </Modal>
        );
    }

    clickRerunAll(): any {
        this.setState({
            confirmMultipleModalOpened: true,
            modalType: 'Rerun',
        });
    }

    clickCancelAll(): any {
        this.setState({
            confirmMultipleModalOpened: true,
            modalType: 'Cancel',
        });
    }

    closeModal(): any {
        this.setState({
            confirmMultipleModalOpened: false,
        });
    }


    render(): React.Element<*> {
        const currentUser = getCurrentUserInfo();
        const allowRerunOrCancel = $c(currentUser)
            .with(x => x.superUserAccessLevel)
            .with(x => [SuperUserAccessLevels.God, SuperUserAccessLevels.Developer].includes(x))
            .return(false);

        const { availableTaskNames, request, loading } = this.state;
        const { searchQuery, results } = this.props;
        const counter = (results && results.totalCount) || 0;

        return (
            <CommonLayout>
                <CommonLayout.GoBack href='/AdminTools'>
                    Вернуться к инструментам администратора
                </CommonLayout.GoBack>
                <CommonLayout.Header data-tid='Header' title='Список задач' />
                <CommonLayout.Content>
                    <ColumnStack block stretch gap={2}>
                        <ColumnStack.Fit>
                            <TaskQueueFilter
                                value={request}
                                availableTaskTypes={availableTaskNames}
                                onChange={value => this.setState({ request: { ...this.state.request, ...value } })}
                                onSearchButtonClick={() => this.handleSearch()}
                            />
                        </ColumnStack.Fit>
                        <ColumnStack.Fit>
                            <Loader type='big' active={loading} data-tid={'Loader'}>
                                {results && <ColumnStack block stretch gap={2}>
                                    {counter > 0 && (
                                        <ColumnStack.Fit>
                                            <RowStack baseline block gap={2}>
                                                <RowStack.Fit>
                                                    Всего результатов: {counter}
                                                </RowStack.Fit>
                                                <RowStack.Fit>
                                                    <RouterLink
                                                        icon='list'
                                                        to={{
                                                            pathname: '/AdminTools/Tasks/Tree',
                                                            search: mapping.stringify(mapping.parse(searchQuery)),
                                                        }}>
                                                        Просмотреть дерево задач
                                                    </RouterLink>
                                                </RowStack.Fit>
                                            </RowStack>

                                        </ColumnStack.Fit>
                                    )}
                                    {counter > 0 && allowRerunOrCancel && (
                                        <ColumnStack.Fit>
                                            <RowStack gap={2} data-tid={'ButtonsWrapper'}>
                                                <RowStack.Fit>
                                                    <Button
                                                        use='danger'
                                                        data-tid={'CancelAllButton'}
                                                        onClick={() => this.clickCancelAll()}>Cancel All</Button>
                                                </RowStack.Fit>
                                                <RowStack.Fit>
                                                    <Button
                                                        use='success'
                                                        data-tid={'RerunAllButton'}
                                                        onClick={() => this.clickRerunAll()}>Rerun All</Button>
                                                </RowStack.Fit>
                                            </RowStack>
                                        </ColumnStack.Fit>
                                    )}
                                    <ColumnStack.Fit>
                                        <TasksTable
                                            getTaskLocation={id => this.getTaskLocation(id)}
                                            allowRerunOrCancel={allowRerunOrCancel}
                                            taskInfos={results.taskMetas}
                                            onRerun={id => this.handleRerunTask(id)}
                                            onCancel={id => this.handleCancelTask(id)}
                                        />
                                    </ColumnStack.Fit>
                                    <ColumnStack.Fit>
                                        <TasksPaginator
                                            nextPageLocation={this.getNextPageLocation()}
                                            prevPageLocation={this.getPrevPageLocation()}
                                        />
                                    </ColumnStack.Fit>
                                </ColumnStack>}
                            </Loader>
                        </ColumnStack.Fit>
                    </ColumnStack>
                    {this.state.confirmMultipleModalOpened && this.renderModal()}
                </CommonLayout.Content>
            </CommonLayout>
        );
    }
}

export default withRouter(withRemoteTaskQueueApi(TasksPageContainer));
