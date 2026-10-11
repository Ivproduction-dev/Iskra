#include <charconv>
#include <cctype>
#include <fstream>
#include <iostream>
#include <limits>
#include <sstream>
#include <string>
#include <type_traits>
#include <unordered_map>
#include <utility>
#include <variant>
#include <vector>

namespace {

using Value = std::variant<long long, std::string>;
using Variables = std::unordered_map<std::string, Value>;

std::string lower_keyword(std::string value) {
    for (std::size_t i = 0; i < value.size(); ++i) {
        const auto byte = static_cast<unsigned char>(value[i]);
        if (byte >= 'A' && byte <= 'Z') {
            value[i] = static_cast<char>(std::tolower(byte));
        }
    }

    const std::pair<std::string, std::string> pairs[] = {
        {"А", "а"}, {"Б", "б"}, {"В", "в"}, {"Г", "г"}, {"Д", "д"},
        {"Е", "е"}, {"Ё", "ё"}, {"Ж", "ж"}, {"З", "з"}, {"И", "и"},
        {"Й", "й"}, {"К", "к"}, {"Л", "л"}, {"М", "м"}, {"Н", "н"},
        {"О", "о"}, {"П", "п"}, {"Р", "р"}, {"С", "с"}, {"Т", "т"},
        {"У", "у"}, {"Ф", "ф"}, {"Х", "х"}, {"Ц", "ц"}, {"Ч", "ч"},
        {"Ш", "ш"}, {"Щ", "щ"}, {"Ъ", "ъ"}, {"Ы", "ы"}, {"Ь", "ь"},
        {"Э", "э"}, {"Ю", "ю"}, {"Я", "я"}
    };
    for (const auto& [upper, lower] : pairs) {
        std::size_t position = 0;
        while ((position = value.find(upper, position)) != std::string::npos) {
            value.replace(position, upper.size(), lower);
            position += lower.size();
        }
    }
    return value;
}

bool parse_integer(const std::string& text, long long& result) {
    if (text.empty()) return false;
    const char* begin = text.data();
    const char* end = begin + text.size();
    if (*begin == '+') {
        ++begin;
        if (begin == end || *begin < '0' || *begin > '9') return false;
    }
    const auto parsed = std::from_chars(begin, end, result);
    return parsed.ec == std::errc{} && parsed.ptr == end;
}

bool parse_string(const std::string& text, std::string& result) {
    if (text.size() < 2 || text.front() != '"' || text.back() != '"') return false;
    result.clear();
    for (std::size_t i = 1; i + 1 < text.size(); ++i) {
        if (text[i] == '\\' && i + 2 < text.size() && (text[i + 1] == '"' || text[i + 1] == '\\')) {
            result += text[i + 1];
            ++i;
        } else {
            result += text[i];
        }
    }
    return true;
}

std::string trim_spaces(std::string value) {
    const auto first = value.find_first_not_of(" \t\r\n");
    if (first == std::string::npos) return {};
    const auto last = value.find_last_not_of(" \t\r\n");
    return value.substr(first, last - first + 1);
}

std::string value_to_text(const Value& value) {
    return std::visit([](const auto& item) {
        if constexpr (std::is_same_v<std::decay_t<decltype(item)>, std::string>) return item;
        else return std::to_string(item);
    }, value);
}

bool parse_expression(const std::string& source, const Variables& variables, Value& result, std::string& error) {
    std::vector<std::string> parts;
    std::size_t start = 0;
    bool in_quotes = false;
    for (std::size_t i = 0; i < source.size(); ++i) {
        if (in_quotes && source[i] == '\\' && i + 1 < source.size() &&
            (source[i + 1] == '"' || source[i + 1] == '\\')) {
            ++i;
        } else if (source[i] == '"') {
            in_quotes = !in_quotes;
        } else if (!in_quotes && source[i] == '.' && i + 1 < source.size() && source[i + 1] == '.') {
            parts.push_back(trim_spaces(source.substr(start, i - start)));
            start = i + 2;
            ++i;
        }
    }
    parts.push_back(trim_spaces(source.substr(start)));

    std::vector<Value> values;
    values.reserve(parts.size());
    for (const auto& part : parts) {
        if (part.empty()) {
            error = "оператор .. должен соединять два значения";
            return false;
        }
        if (part.front() == '"') {
            std::string text;
            if (!parse_string(part, text)) {
                error = "ожидается текст в кавычках";
                return false;
            }
            values.emplace_back(std::move(text));
            continue;
        }
        long long number = 0;
        if (parse_integer(part, number)) {
            values.emplace_back(number);
            continue;
        }
        if (part.find_first_of(" \t\r\n") != std::string::npos) {
            error = "не удалось прочитать значение «" + part + "»";
            return false;
        }
        const auto found = variables.find(part);
        if (found == variables.end()) {
            error = "переменная «" + part + "» не задана";
            return false;
        }
        values.push_back(found->second);
    }

    if (values.size() == 1) {
        result = std::move(values.front());
        return true;
    }
    std::string combined;
    for (const auto& value : values) combined += value_to_text(value);
    result = std::move(combined);
    return true;
}

bool run_block(const std::vector<std::pair<std::size_t, std::string>>& lines, std::size_t from, std::size_t to, Variables& variables, int depth);

bool run_line(const std::string& source, Variables& variables, std::size_t line_number) {
    std::istringstream input(source);
    std::string command;
    if (!(input >> command)) return true;

    const auto keyword = lower_keyword(command);
    const auto fail = [line_number](const std::string& message) {
        std::cerr << "Строка " << line_number << ": " << message << '\n';
        return false;
    };

    if (keyword == "задать" || keyword == "присвоить") {
        std::string name;
        std::string equals;
        if (!(input >> name >> equals) || equals != "=") {
            return fail("ожидается: Задать имя = значение");
        }
        std::string rest;
        std::getline(input, rest);
        rest = trim_spaces(std::move(rest));
        if (rest.empty()) {
            return fail("ожидается: Задать имя = значение");
        }
        Value value = 0LL;
        std::string error;
        if (!parse_expression(rest, variables, value, error)) return fail(error);
        variables[name] = std::move(value);
        return true;
    }

    if (keyword == "изменить") {
        std::string name;
        std::string amount_text;
        std::string extra;
        if (!(input >> name >> amount_text) || (input >> extra)) {
            return fail("ожидается: Изменить имя число");
        }
        const auto found = variables.find(name);
        if (found == variables.end()) return fail("переменная «" + name + "» не задана");
        if (!std::holds_alternative<long long>(found->second)) {
            return fail("переменная «" + name + "» не число");
        }
        long long amount = 0;
        if (!parse_integer(amount_text, amount)) return fail("ожидается целое число, например 1 или -1");
        const auto current = std::get<long long>(found->second);
        if ((amount > 0 && current > std::numeric_limits<long long>::max() - amount) ||
            (amount < 0 && current < std::numeric_limits<long long>::min() - amount)) {
            return fail("переполнение целого числа");
        }
        found->second = std::get<long long>(found->second) + amount;
        return true;
    }

    if (keyword == "печать") {
        std::string expression;
        std::getline(input, expression);
        expression = trim_spaces(std::move(expression));
        Value value = 0LL;
        std::string error;
        if (!parse_expression(expression, variables, value, error)) return fail(error);
        std::visit([](const auto& item) { std::cout << item << '\n'; }, value);
        return true;
    }

    if (keyword == "спросить") {
        std::string rest;
        std::getline(input, rest);
        rest = trim_spaces(std::move(rest));
        std::vector<std::string> parts;
        std::size_t start = 0;
        while (true) {
            const auto dot = rest.find(" . ", start);
            if (dot == std::string::npos) {
                parts.push_back(trim_spaces(rest.substr(start)));
                break;
            }
            parts.push_back(trim_spaces(rest.substr(start, dot - start)));
            start = dot + 3;
        }
        if (parts.size() != 3) {
            return fail("ожидается: Спросить вопрос? . Да . Нет сохранить в имя");
        }
        std::string question = parts[0];
        if (!question.empty() && question.front() == '"') {
            std::string parsed;
            if (!parse_string(question, parsed)) return fail("ожидается текст в кавычках");
            question = std::move(parsed);
        }
        const std::string button1 = parts[1];
        const std::string save_marker = " сохранить в ";
        const auto save_at = parts[2].rfind(save_marker);
        if (save_at == std::string::npos) {
            return fail("ожидается: ... . Нет сохранить в имя");
        }
        const std::string button2 = trim_spaces(parts[2].substr(0, save_at));
        const std::string save_name = trim_spaces(parts[2].substr(save_at + save_marker.size()));
        if (button1.empty() || button2.empty()) {
            return fail("нужны две кнопки, например . Да . Нет");
        }
        if (save_name.empty() || save_name.find_first_of(" \t") != std::string::npos) {
            return fail("после «сохранить в» нужно одно имя");
        }
        if (!question.empty()) {
            std::cout << question << '\n';
        }
        std::cout << "1. " << button1 << '\n';
        std::cout << "2. " << button2 << '\n';
        std::string answer;
        std::string chosen;
        while (true) {
            if (!std::getline(std::cin, answer)) {
                return fail("нет ответа");
            }
            answer = trim_spaces(std::move(answer));
            if (answer == "1" || answer == button1) {
                chosen = "да";
                break;
            }
            if (answer == "2" || answer == button2) {
                chosen = "нет";
                break;
            }
            std::cout << "Нужно 1 или 2" << '\n';
        }
        variables[save_name] = std::move(chosen);
        return true;
    }

    return fail("неизвестная команда «" + command + "»");
}

bool is_if_keyword(const std::string& keyword) {
    return keyword == "если" || keyword == "есле" || keyword == "если";
}

bool is_end_keyword(const std::string& keyword) {
    const std::string plain = "конец";
    if (keyword == plain) return true;
    const std::string tail = "если";
    return keyword.size() == plain.size() + tail.size() &&
        keyword.compare(0, plain.size(), plain) == 0 &&
        keyword.compare(plain.size(), tail.size(), tail) == 0;
}

bool resolve_condition_value(const std::string& token, const Variables& variables, Value& result, std::size_t line_number, std::string& error) {
    (void)line_number;
    return parse_expression(token, variables, result, error);
}

bool compare_values(const Value& left, const std::string& op, const Value& right, bool& result, std::string& error) {
    if (std::holds_alternative<long long>(left) && std::holds_alternative<long long>(right)) {
        const auto a = std::get<long long>(left);
        const auto b = std::get<long long>(right);
        if (op == "=") result = (a == b);
        else if (op == "!=") result = (a != b);
        else if (op == ">") result = (a > b);
        else if (op == "<") result = (a < b);
        else if (op == ">=") result = (a >= b);
        else if (op == "<=") result = (a <= b);
        else {
            error = "неизвестное сравнение «" + op + "»";
            return false;
        }
        return true;
    }
    if (std::holds_alternative<std::string>(left) && std::holds_alternative<std::string>(right)) {
        const auto& a = std::get<std::string>(left);
        const auto& b = std::get<std::string>(right);
        if (op == "=") result = (a == b);
        else if (op == "!=") result = (a != b);
        else {
            error = "тексты можно только сравнить на равенство (=, !=)";
            return false;
        }
        return true;
    }
    error = "нельзя сравнить число и текст";
    return false;
}

bool eval_if_tokens(const std::vector<std::string>& tokens, const Variables& variables, bool& condition, std::string& error) {
    if (tokens.size() < 5) {
        error = "ожидается: Если значение сравнение значение Тогда";
        return false;
    }
    const auto head = lower_keyword(tokens[0]);
    if ((head != "иначеесли" && head != "пока" && !is_if_keyword(head)) || lower_keyword(tokens[4]) != "тогда") {
        if (lower_keyword(tokens.back()) != "тогда") {
            error = "ожидается: условие и слово Тогда";
            return false;
        }
    }
    std::size_t operator_index = tokens.size();
    for (std::size_t i = 1; i + 1 < tokens.size(); ++i) {
        if (tokens[i] == "=" || tokens[i] == "!=" || tokens[i] == ">" || tokens[i] == "<" ||
            tokens[i] == ">=" || tokens[i] == "<=") {
            if (operator_index != tokens.size()) {
                error = "ожидается одно сравнение";
                return false;
            }
            operator_index = i;
        }
    }
    if (operator_index == tokens.size() || operator_index == 1 || operator_index + 2 >= tokens.size()) {
        error = "ожидается: значение сравнение значение Тогда";
        return false;
    }
    const auto join_tokens = [&tokens](std::size_t from, std::size_t to) {
        std::string value;
        for (auto i = from; i < to; ++i) value += tokens[i];
        return value;
    };
    const auto left_expression = join_tokens(1, operator_index);
    const auto right_expression = join_tokens(operator_index + 1, tokens.size() - 1);
    const auto& op = tokens[operator_index];
    Value left = 0LL;
    Value right = 0LL;
    if (!resolve_condition_value(left_expression, variables, left, 0, error)) return false;
    if (!resolve_condition_value(right_expression, variables, right, 0, error)) return false;
    return compare_values(left, op, right, condition, error);
}

std::vector<std::string> split_condition(std::string text) {
    std::vector<std::string> tokens;
    std::size_t i = 0;
    while (i < text.size()) {
        while (i < text.size() && std::isspace(static_cast<unsigned char>(text[i]))) ++i;
        if (i >= text.size()) break;
        if (text[i] == '"') {
            std::size_t j = i + 1;
            while (j < text.size()) {
                if (text[j] == '\\' && j + 1 < text.size() && (text[j + 1] == '"' || text[j + 1] == '\\')) {
                    j += 2;
                } else if (text[j] == '"') {
                    break;
                } else {
                    ++j;
                }
            }
            if (j >= text.size()) {
                tokens.push_back(text.substr(i));
                break;
            }
            tokens.push_back(text.substr(i, j - i + 1));
            i = j + 1;
        } else {
            std::size_t j = i;
            while (j < text.size() && !std::isspace(static_cast<unsigned char>(text[j]))) ++j;
            tokens.push_back(text.substr(i, j - i));
            i = j;
        }
    }
    return tokens;
}

std::string first_keyword(const std::string& source) {
    std::istringstream input(source);
    std::string command;
    if (input >> command) return lower_keyword(command);
    return {};
}

std::string strip_comment(const std::string& source) {
    bool in_quotes = false;
    for (std::size_t i = 0; i < source.size(); ++i) {
        const char c = source[i];
        if (in_quotes) {
            if (c == '\\' && i + 1 < source.size() && (source[i + 1] == '"' || source[i + 1] == '\\')) {
                ++i;
            } else if (c == '"') {
                in_quotes = false;
            }
        } else if (c == '"') {
            in_quotes = true;
        } else if (c == '#') {
            return source.substr(0, i);
        } else if (c == '/' && i + 1 < source.size() && source[i + 1] == '/') {
            return source.substr(0, i);
        }
    }
    return source;
}

std::string normalize_event_name(const std::string& source) {
    std::string lowered = lower_keyword(source);
    std::string result;
    bool gap = true;
    for (const char c : lowered) {
        const bool space = c == ' ' || c == '\t' || c == '\r' || c == '\n';
        if (space) {
            gap = true;
        } else {
            if (gap && !result.empty()) result += ' ';
            result += c;
            gap = false;
        }
    }
    return result;
}

bool is_start_event(const std::string& name) {
    return name == "запуск" || name == "запуск сцены";
}

bool run_block(const std::vector<std::pair<std::size_t, std::string>>& lines, std::size_t from, std::size_t to, Variables& variables, int depth) {
    if (depth > 256) {
        std::cerr << "Строка " << lines[from].first << ": слишком глубокая вложенность" << '\n';
        return false;
    }
    std::size_t pc = from;
    while (pc < to) {
        const auto line_number = lines[pc].first;
        const auto source = strip_comment(lines[pc].second);
        const auto keyword = first_keyword(source);
        const auto fail = [line_number](const std::string& message) {
            std::cerr << "Строка " << line_number << ": " << message << '\n';
            return false;
        };

        if (keyword == "при") {
            if (depth > 0) {
                return fail("событие внутри события");
            }
            std::istringstream header(source);
            std::string head_word;
            header >> head_word;
            std::string rest;
            std::getline(header, rest);
            const auto event_name = normalize_event_name(rest);
            if (event_name.empty()) {
                return fail("ожидается: При запуск");
            }

            std::size_t nesting = 1;
            std::size_t end_pos = to;
            for (std::size_t j = pc + 1; j < to; ++j) {
                const auto inner = first_keyword(lines[j].second);
                if (inner == "при") {
                    std::cerr << "Строка " << lines[j].first << ": событие внутри события" << '\n';
                    return false;
                }
                if (is_if_keyword(inner) || inner == "пока") {
                    ++nesting;
                } else if (is_end_keyword(inner)) {
                    if (--nesting == 0) {
                        end_pos = j;
                        break;
                    }
                }
            }
            if (end_pos == to) return fail("нет Конец для события");
            if (is_start_event(event_name)) {
                if (!run_block(lines, pc + 1, end_pos, variables, depth + 1)) return false;
            }
            pc = end_pos + 1;
            continue;
        }

        if (keyword == "пока") {
            const auto tokens = split_condition(source);
            bool first_check = false;
            std::string error;
            if (!eval_if_tokens(tokens, variables, first_check, error)) return fail(error);
            (void)first_check;

            std::size_t nesting = 1;
            std::size_t end_pos = to;
            for (std::size_t j = pc + 1; j < to; ++j) {
                const auto inner = first_keyword(lines[j].second);
                if (is_if_keyword(inner) || inner == "пока") {
                    ++nesting;
                } else if (is_end_keyword(inner)) {
                    if (--nesting == 0) {
                        end_pos = j;
                        break;
                    }
                }
            }
            if (end_pos == to) return fail("нет Конец для Пока");
            for (;;) {
                bool condition = false;
                if (!eval_if_tokens(tokens, variables, condition, error)) return fail(error);
                if (!condition) break;
                if (!run_block(lines, pc + 1, end_pos, variables, depth + 1)) return false;
            }
            pc = end_pos + 1;
            continue;
        }

        if (is_if_keyword(keyword)) {
            const auto tokens = split_condition(source);
            bool condition = false;
            std::string error;
            if (!eval_if_tokens(tokens, variables, condition, error)) return fail(error);

            std::size_t nesting = 1;
            std::vector<std::size_t> branches;
            std::size_t else_pos = to;
            std::size_t end_pos = to;
            for (std::size_t j = pc + 1; j < to; ++j) {
                const auto inner = first_keyword(lines[j].second);
                if (is_if_keyword(inner) || inner == "пока") {
                    ++nesting;
                } else if (is_end_keyword(inner)) {
                    if (--nesting == 0) {
                        end_pos = j;
                        break;
                    }
                } else if (nesting == 1) {
                    if (inner == "иначеесли") {
                        branches.push_back(j);
                    } else if (inner == "иначе" && else_pos == to) {
                        else_pos = j;
                    }
                }
            }
            if (end_pos == to) return fail("нет Конец для Если");

            std::size_t run_from = to;
            std::size_t run_to = to;
            if (condition) {
                run_from = pc + 1;
                run_to = !branches.empty() ? branches[0] : (else_pos == to ? end_pos : else_pos);
            } else {
                bool matched = false;
                for (std::size_t b = 0; b < branches.size() && !matched; ++b) {
                    const auto branch_tokens = split_condition(lines[branches[b]].second);
                    bool branch_condition = false;
                    if (!eval_if_tokens(branch_tokens, variables, branch_condition, error)) {
                        std::cerr << "Строка " << lines[branches[b]].first << ": " << error << '\n';
                        return false;
                    }
                    if (branch_condition) {
                        matched = true;
                        run_from = branches[b] + 1;
                        run_to = (b + 1 < branches.size()) ? branches[b + 1] : (else_pos == to ? end_pos : else_pos);
                    }
                }
                if (!matched && else_pos != to) {
                    run_from = else_pos + 1;
                    run_to = end_pos;
                }
            }
            if (run_from != to) {
                if (!run_block(lines, run_from, run_to, variables, depth + 1)) return false;
            }
            pc = end_pos + 1;
            continue;
        }

        if (keyword == "иначе" || keyword == "иначеесли" || is_end_keyword(keyword)) {
            return fail("«" + keyword + "» без Если");
        }

        if (!run_line(source, variables, line_number)) return false;
        ++pc;
    }
    return true;
}

}

#ifndef ISKRA_TEST_BUILD
int main(int argc, char* argv[]) {
    if (argc != 2) {
        std::cerr << "Использование: iskra <файл.isk>\n";
        return 2;
    }

    std::ifstream file(argv[1]);
    if (!file) {
        std::cerr << "Не удалось открыть файл: " << argv[1] << '\n';
        return 2;
    }

    Variables variables;
    std::vector<std::pair<std::size_t, std::string>> lines;
    std::string line;
    std::size_t line_number = 0;
    while (std::getline(file, line)) {
        ++line_number;
        if (line_number == 1 && line.size() >= 3 &&
            static_cast<unsigned char>(line[0]) == 0xEF &&
            static_cast<unsigned char>(line[1]) == 0xBB &&
            static_cast<unsigned char>(line[2]) == 0xBF) {
            line.erase(0, 3);
        }
        lines.emplace_back(line_number, line);
    }
    if (file.bad()) {
        std::cerr << "Ошибка чтения файла\n";
        return 2;
    }
    if (!run_block(lines, 0, lines.size(), variables, 0)) return 1;
    return 0;
}
#endif
