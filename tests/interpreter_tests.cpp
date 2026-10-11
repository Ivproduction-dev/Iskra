#define ISKRA_TEST_BUILD
#include "../main.cpp"

namespace {

int failures = 0;

void check(bool condition, const std::string& name) {
    if (condition) {
        std::cout << "ПРОЙДЕН: " << name << '\n';
    } else {
        std::cerr << "ОШИБКА: " << name << '\n';
        ++failures;
    }
}

void test_concatenation() {
    Variables variables{
        {"имя", std::string("Искра")},
        {"счёт", 12LL}
    };
    Value result = 0LL;
    std::string error;

    check(parse_expression("\"Привет, \" .. имя .. \"! Счёт: \"..счёт", variables, result, error) &&
        std::holds_alternative<std::string>(result) &&
        std::get<std::string>(result) == "Привет, Искра! Счёт: 12",
        "текст, переменные и числа склеиваются с пробелами и без них");
}

void test_expression_errors() {
    Variables variables;
    Value result = 0LL;
    std::string error;

    check(!parse_expression("\"начало\"..", variables, result, error) && !error.empty(),
        "правая часть оператора обязательна");
    error.clear();
    check(!parse_expression("нетТакойПеременной", variables, result, error) && !error.empty(),
        "неизвестная переменная возвращает понятную ошибку");
}

void test_assignment_and_print() {
    Variables variables;
    std::ostringstream output;
    auto* original_output = std::cout.rdbuf(output.rdbuf());

    const bool assignment_ok = run_line("Задать имя = \"Мир\"", variables, 1);
    const bool message_ok = run_line("Задать приветствие = \"Привет, \"..имя", variables, 2);
    const bool print_ok = run_line("Печать приветствие", variables, 3);

    std::cout.rdbuf(original_output);
    check(assignment_ok && message_ok && print_ok && output.str() == "Привет, Мир\n",
        "присваивание склеенного значения и Печать работают вместе");
}

void test_comparison_with_concatenation() {
    Variables variables{{"имя", std::string("Искра")}};
    Value left = 0LL;
    Value right = 0LL;
    std::string error;
    bool equal = false;

    const bool values_ok = resolve_condition_value("\"Привет, \"..имя", variables, left, 0, error) &&
        resolve_condition_value("\"Привет, Искра\"", variables, right, 0, error);
    const bool compare_ok = values_ok && compare_values(left, "=", right, equal, error);
    check(compare_ok && equal, "склеенное значение сравнивается в условии");
}

}

int main() {
    test_concatenation();
    test_expression_errors();
    test_assignment_and_print();
    test_comparison_with_concatenation();
    if (failures != 0) {
        std::cerr << "Провалено проверок: " << failures << '\n';
        return 1;
    }
    std::cout << "Все проверки пройдены.\n";
    return 0;
}
