import { Button, Modal, ThemeContext } from "@skbkontur/react-ui";
import React from "react";

import { useCustomSettings } from "../../CustomSettingsContext";

import { jsStyles } from "./TaskQueueFilter.styles";

interface TaskQueueFilterHelpModalProps {
    onClose: () => void;
}

export const TaskQueueFilterHelpModal = ({ onClose }: TaskQueueFilterHelpModalProps) => {
    const theme = React.useContext(ThemeContext);
    const { customSearchHelp } = useCustomSettings();

    return (
        <Modal data-tid="Modal" onClose={onClose} width={900}>
            <Modal.Header>
                <span className={jsStyles.modalText(theme)}>Справка</span>
            </Modal.Header>
            <Modal.Body>
                {customSearchHelp || (
                    <div className={jsStyles.modalText(theme)}>
                        При поиске задач можно пользоваться следующими инструментами поиска:
                        <ol className={jsStyles.modalList()}>
                            <li>
                                Ввод значения без дополнительных указаний. В этом случае найдутся все задачи, в
                                текстовых полях которых встречается это значение.
                            </li>
                            <li>
                                Операторы AND, OR, NOT, скобки между ними. Например:{" "}
                                <code>(value1 OR value2) AND NOT value3</code>.
                            </li>
                            <li>
                                Ввод значения с указанием конкретного поля, в котором его нужно искать. Поля есть двух
                                типов: Meta и Data.SomeTaskName, в полях Meta лежит общая информация по задаче, в полях
                                Data.SomeTaskName - информация по типу задачи.
                                <br />
                                Например: <code>Meta.Id:value</code>,{" "}
                                <code>Data.FtpMessageDeliveryFinished.DocumentCirculationId:value</code>,
                                <code>Data.\*.DocumentCirculationId:(value1 OR value2)</code>. Конкретный список полей в
                                Meta можно узнать в RemoteTaskQueue.Monitoring.Storage.RtqElastcisearchSchema
                            </li>
                            <li>
                                Знаки * и ?. Например, <code>Meta.Name:val*</code> или <code>Meta.Name:val?e</code>.
                                Звездочка не может быть в начале искомого значения (<code>Meta.Name:*lue</code> не
                                работает).
                                <br />
                                Знаки можно использовать не только в значениях, но и при указании полей. По запросу{" "}
                                <code>Data.DocumentCirculationId.\*Id:value</code> во всех поля даты, которые
                                заканчиваются на id найдётся value.
                            </li>
                            <li>
                                Задание интервалов для дат. Квадратные скобки при задании интервала означают, что концы
                                включены, фигурные - что исключены. Звездочка показывает, что интервал в эту сторону
                                бесконечен.
                                <br />
                                Например: <code>date:[2012-01-01 TO 2012-12-31]</code>,
                                <code>date:&#123;* TO 2012-01-01&#125;.</code>
                            </li>
                            <li>
                                Задание интервалов для чисел аналогично заданию интервалов для дат. Например:{" "}
                                <code>count:[1 TO 5&#125;</code>.
                            </li>
                            <li>
                                Сравнение чисел. Например: <code>age:&gt;=10</code>,<code>age:&lt;=10</code>,{" "}
                                <code>age:(&gt;=10 AND &lt; 20)</code>.
                            </li>
                            <li>
                                Проверка полей на заполненность. Например: <code>NOT _exists_:Meta.ParentTaskId</code>-
                                поле пусто, <code>_exists_:Meta.ParentTaskId</code> - поле заполнено.
                            </li>
                        </ol>
                    </div>
                )}
            </Modal.Body>
            <Modal.Footer>
                <div className={jsStyles.modalFooter()}>
                    <Button onClick={onClose}>Закрыть</Button>
                </div>
            </Modal.Footer>
        </Modal>
    );
};
